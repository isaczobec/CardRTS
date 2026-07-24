using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the WorldGenHandler and drives world generation + rendering.
/// Assign Renderer in the Inspector. Call GenerateAndRender(connectedClientIds, seed) once
/// the game starts (see NetworkManager, which is the source of truth for both).
/// </summary>
public class WorldManager : Singleton<WorldManager>
{
    public WorldRenderer Renderer;

    [SerializeField] TileSettings[] _tileSettings;

    public WorldGenHandler Handler { get; private set; }

    // Indexed by (int)TileType for O(1) lookup. Built in GenerateAndRender.
    TileSettings[] _settingsByType;

    /// <summary>
    /// Converts tile coordinates to a world-space Vector3.
    /// </summary>
    /// <param name="tileX">Tile X coordinate.</param>
    /// <param name="tileY">Tile Y coordinate.</param>
    /// <param name="center">If true, returns the center of the tile; otherwise returns the corner (origin).</param>
    /// <param name="useHeightmap">If true, samples the heightmap for the Y component. Center uses the average of the four corner heights.</param>
    public Vector3 TileToWorldPosition(ushort tileX, ushort tileY, bool center = false, bool useHeightmap = true)
    {
        float h = 0f;
        if (useHeightmap && Handler != null)
        {
            if (center)
            {
                ushort maxCoord = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
                ushort x1 = (ushort)Mathf.Min(tileX + 1, maxCoord);
                ushort y1 = (ushort)Mathf.Min(tileY + 1, maxCoord);
                h = (Handler.GetHeight(tileX, tileY) +
                     Handler.GetHeight(x1,    tileY) +
                     Handler.GetHeight(tileX,    y1) +
                     Handler.GetHeight(x1,       y1)) * 0.25f;
            }
            else
            {
                h = Handler.GetHeight(tileX, tileY);
            }
        }
        float offset = center ? 0.5f : 0f;
        return new Vector3(tileX + offset, h, tileY + offset);
    }

    // connectedClientIds and seed must be identical on every machine generating this world
    // (server and every client) — world gen is deterministic given the same inputs, and
    // features like SpawnPlayerBasesFeature/RemapTilesNearPointsFeature/EntityClusterFeature
    // depend on them, so a mismatch would desync the actual terrain, not just entities. The
    // caller is responsible for supplying the same values everywhere: NetworkManager threads
    // them through the GameStart message explicitly rather than letting each side derive/
    // generate them independently (a client's ECS has no player entities yet at this point
    // in the handshake, and a client-generated random seed obviously wouldn't match the
    // server's — see NetworkManager.OnGameStart).
    public void GenerateAndRender(List<ushort> connectedClientIds, int seed)
    {
        Handler = new WorldGenHandler();
        Handler.ConnectedClientIds = connectedClientIds ?? new List<ushort>();
        Handler.Seed = seed;
        BuildTileSettingsLookup();
        SetupWorldGen(Handler);
        Handler.Generate();
        NavMeshHandler.instance.CreateNavMesh(this);
        Renderer.Render(Handler);

        // Actions are server-only: entities spawned here are included in the initial
        // ECS snapshot sent to clients, so they propagate automatically.
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (isServer)
            Handler.ExecuteActions(TickManager.instance.ECS);
    }

    // Builds a TileType -> TileType map from every colliding tile type to its nearest (by
    // declared enum order) non-colliding tile type — a generic way to "scrub obstacles"
    // from an area without needing to know which concrete TileTypes are configured to
    // collide. Used to keep the area around each player base clear (see
    // RemapTilesNearPointsFeature in SetupWorldGen).
    public Dictionary<TileType, TileType> BuildCollisionClearingMap()
    {
        var map = new Dictionary<TileType, TileType>();
        TileType[] allTypes = (TileType[])System.Enum.GetValues(typeof(TileType));

        foreach (TileType type in allTypes)
        {
            if (!GetTileSettings(type).HasCollision) continue;

            TileType? closest = null;
            int bestDistance = int.MaxValue;
            foreach (TileType candidate in allTypes)
            {
                if (GetTileSettings(candidate).HasCollision) continue;
                int distance = Mathf.Abs((int)candidate - (int)type);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = candidate;
                }
            }

            if (closest.HasValue)
                map[type] = closest.Value;
        }

        return map;
    }

    void BuildTileSettingsLookup()
    {
        int count = System.Enum.GetValues(typeof(TileType)).Length;
        _settingsByType = new TileSettings[count];
        if (_tileSettings != null)
            foreach (var s in _tileSettings)
                _settingsByType[(int)s.Type] = s;
    }

    public TileSettings GetTileSettings(TileType type) => _settingsByType[(int)type];

    // Returns every WorldGenFeature of type T currently registered on Handler (including
    // ones enqueued as biome children — see BiomeFeature.Generate/EnqueueFeature) for which
    // predicate (if given) returns true. Mirrors WorldGenHandler.GetActions's shape.
    public List<T> GetFeatures<T>(Func<T, bool> predicate = null) where T : WorldGenFeature
    {
        var result = new List<T>();
        if (Handler == null) return result;
        foreach (var feature in Handler.features)
            if (feature is T typed && (predicate == null || predicate(typed)))
                result.Add(typed);
        return result;
    }

    public bool HasCollision(ushort tileX, ushort tileY)
        => _settingsByType[(int)Handler.GetTileType(tileX, tileY)].HasCollision;

    void SetupWorldGen(WorldGenHandler handler)
    {
        // Tracks every spawned entity's position for fast radius/closest-point lookups (see
        // EntityClusterFeature.MinDistanceToOtherEntities) — added first so it's present no
        // matter which feature enqueues the first EntitySpawnAction.
        handler.AddResource(new SpawnedEntityRegistry());

        // Spawn player bases first, before any terrain/entity features run, so everything
        // added below can see where they ended up (via GetPreviousFeature) if it needs to.
        handler.features.Add(new SpawnPlayerBasesFeature());

        // Every noise generator below samples Mathf.PerlinNoise, which is itself a pure
        // (deterministic) function of its input coordinates — the only thing that ever made
        // two generations look different was each generator's own OffsetX/OffsetY shifting
        // WHERE in that noise field gets sampled. Previously those were hardcoded literals,
        // so every world reused the exact same offsets (hence the exact same biome/terrain
        // layout) regardless of Seed. Drawing them from a Random seeded with handler.Seed
        // instead — in this fixed order, which never changes since SetupWorldGen always
        // builds the same resource list (same reasoning as WorldGenHandler.Random's own doc
        // comment) — makes the layout depend on Seed while staying fully reproducible for a
        // given one. This can't just reuse handler.Random itself: that's only created once
        // WorldGenHandler.Generate() actually starts running (after SetupWorldGen returns),
        // so this draws from its own, separately-seeded instance instead.
        System.Random offsetRandom = new System.Random(handler.Seed);

        // --- Terrain noise ---
        handler.AddResource(new PerlinNoiseGenerator("terrain")
        {
            Scale   = 0.04f,
            OffsetX = offsetRandom.Next(0, 10000),
            OffsetY = offsetRandom.Next(0, 10000),
        });

        // --- Biome axes: temperature (X) and humidity (Y) ---
        handler.AddResource(new PerlinNoiseGenerator("biomeTemp")
        {
            Scale   = 0.005f,
            OffsetX = offsetRandom.Next(0, 10000),
            OffsetY = offsetRandom.Next(0, 10000),
        });
        handler.AddResource(new PerlinNoiseGenerator("biomeHumidity")
        {
            Scale   = 0.005f,
            OffsetX = offsetRandom.Next(0, 10000),
            OffsetY = offsetRandom.Next(0, 10000),
        });

        // Ridged (thin, winding vein) noise for TundraBiome's tunnel-like mountain
        // formations — a different offset from "terrain" so the veins don't line up with
        // Wetland's own Mountain-patch noise.
        handler.AddResource(new RidgedNoiseGenerator("tundraRidges")
        {
            Scale   = 0.06f,
            OffsetX = offsetRandom.Next(0, 10000),
            OffsetY = offsetRandom.Next(0, 10000),
        });

        // Coarse, low-frequency mask gating where the ridged vein noise above is even
        // allowed to paint Mountain — without this the veins form one continuous network
        // across the whole biome; masked, they only show up within scattered patches.
        handler.AddResource(new PerlinNoiseGenerator("tundraMountainPatchMask")
        {
            Scale   = 0.02f,
            OffsetX = offsetRandom.Next(0, 10000),
            OffsetY = offsetRandom.Next(0, 10000),
        });

        // Desert biome: hot (high temp) and dry (low humidity).
        handler.features.Add(new BiomeFeature
        {
            NoiseKeyX = "biomeTemp",
            NoiseKeyY = "biomeHumidity",
            MinX = 0.60f, MaxX = 1.00f,
            MinY = 0.00f, MaxY = 1.00f,
            ChildFeatures = new WorldGenFeature[]
            {
                // Repaint interior terrain with arid thresholds (more sand, no grass).
                // Water is preserved so existing lakes/rivers remain as oases.
                new BiomeBorderFillFeature { Threshold = 1.0f, FillType = TileType.Sand },
                // Stones are the desert's dominant resource — a bit more common here than
                // trees/ore, which still both appear.
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnRock,
                    ClusterCountMin       = 6,
                    ClusterCountMax       = 8,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 5f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 1.5f,
                    AllowedTileTypes      = new[] { TileType.Sand },
                },
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnTree,
                    ClusterCountMin       = 4,
                    ClusterCountMax       = 6,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 15f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 6f,
                    AllowedTileTypes      = new[] { TileType.Sand },
                },
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnOre,
                    ClusterCountMin       = 4,
                    ClusterCountMax       = 6,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 5f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 1.5f,
                    AllowedTileTypes      = new[] { TileType.Sand },
                },
            }
        });

        // Wetland biome: cold (low temp) and humid (high humidity) — shares the cold half of
        // the temperature axis with TundraBiome below, split by humidity (dry -> tundra).
        handler.features.Add(new BiomeFeature
        {
            NoiseKeyX = "biomeTemp",
            NoiseKeyY = "biomeHumidity",
            MinX = 0.00f, MaxX = 0.60f,
            MinY = 0.50f, MaxY = 1.00f,
            ChildFeatures = new WorldGenFeature[]
            {
                // Repaint interior terrain with lush thresholds (more grass, less sand).
                // Water is preserved so rivers flow through the wetland naturally.
                new BiomeBorderFillFeature { Threshold = 1.0f, FillType = TileType.Grass },
                new NoiseTileFeature
                {
                    NoiseResourceKey = "terrain",
                    NoiseMinThreshold = 0.75f,
                    Thresholds = new List<NoiseThreshold>
                    {
                        new NoiseThreshold { MaxValue = 1f, Type = TileType.Mountain },
                    },
                },
                // Trees are the wetland's dominant resource — a bit more common here than
                // stones/ore, which still both appear.
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnTree,
                    ClusterCountMin       = 6,
                    ClusterCountMax       = 8,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 15f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 6f,
                    AllowedTileTypes      = new[] { TileType.Grass },
                },
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnRock,
                    ClusterCountMin       = 4,
                    ClusterCountMax       = 6,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 5f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 1.5f,
                    AllowedTileTypes      = new[] { TileType.Grass },
                },
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnOre,
                    ClusterCountMin       = 4,
                    ClusterCountMax       = 6,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 5f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 1.5f,
                    AllowedTileTypes      = new[] { TileType.Grass },
                },
            }
        });

        // Tundra biome: cold (low temp) and dry (low humidity) — mainly snow, with scattered
        // ice patches (both walkable) and ridged-noise mountain formations that read as thin,
        // winding tunnels/corridors rather than round blobs, for interesting chase gameplay.
        handler.features.Add(new BiomeFeature
        {
            NoiseKeyX = "biomeTemp",
            NoiseKeyY = "biomeHumidity",
            MinX = 0.00f, MaxX = 0.60f,
            MinY = 0.00f, MaxY = 0.50f,
            ChildFeatures = new WorldGenFeature[]
            {
                // Base terrain: mostly snow.
                new BiomeBorderFillFeature { Threshold = 1.0f, FillType = TileType.Snow },
                // Scattered ice spots — also walkable, just visually/thematically distinct.
                new NoiseTileFeature
                {
                    NoiseResourceKey = "terrain",
                    NoiseMinThreshold = 0.7f,
                    Thresholds = new List<NoiseThreshold>
                    {
                        new NoiseThreshold { MaxValue = 1f, Type = TileType.Ice },
                    },
                },
                // Thin, winding mountain veins (see RidgedNoiseGenerator), masked down to
                // small scattered patches (see tundraMountainPatchMask above) instead of one
                // continuous network — the gaps within/around each patch form natural
                // corridors for chasing/fleeing troops to funnel through.
                new NoiseTileFeature
                {
                    NoiseResourceKey = "tundraRidges",
                    NoiseMinThreshold = 0.85f,
                    MaskNoiseResourceKey = "tundraMountainPatchMask",
                    MaskMinThreshold = 0.7f,
                    Thresholds = new List<NoiseThreshold>
                    {
                        new NoiseThreshold { MaxValue = 1f, Type = TileType.Mountain },
                    },
                },
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnTree,
                    ClusterCountMin       = 4,
                    ClusterCountMax       = 6,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 15f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 6f,
                    AllowedTileTypes      = new[] { TileType.Snow },
                },
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnRock,
                    ClusterCountMin       = 4,
                    ClusterCountMax       = 6,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 5f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 1.5f,
                    AllowedTileTypes      = new[] { TileType.Snow },
                },
                // Ore is the tundra's dominant resource — a bit more common here than
                // trees/stones, which still both appear.
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnOre,
                    ClusterCountMin       = 6,
                    ClusterCountMax       = 8,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 5f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 1.5f,
                    AllowedTileTypes      = new[] { TileType.Snow },
                },
            }
        });

        // Neutral soulstone objective cluster, fixed at the exact map center — after terrain/
        // biome generation so it's placed on top of whatever terrain ended up there.
        handler.features.Add(new SoulstoneClusterFeature());

        // One gem-deposit cluster per base, symmetric around the map center — must run after
        // SpawnPlayerBasesFeature (reads its Bases via GetPreviousFeature).
        handler.features.Add(new GemClusterFeature());

        // --- End of generation: clear a landing zone around every player base ---

        // No collidable tiles (water/mountain/etc., whatever's configured) within reach of
        // a base, so a player is never boxed in by their own spawn.
        handler.features.Add(new RemapTilesNearPointsFeature
        {
            Points = h =>
            {
                var basesFeature = h.GetPreviousFeature<SpawnPlayerBasesFeature>();
                if (basesFeature == null) return Array.Empty<(float, float)>();

                var points = new List<(float, float)>();
                foreach (var b in basesFeature.Bases)
                    points.Add((b.X, b.Y));
                return points;
            },
            Radius = 10f,
            TileMap = BuildCollisionClearingMap(),
        });

        // No trees/other spawned entities overlapping a base — must run after every
        // feature above that could have placed one nearby (EntityClusterFeature, etc.).
        handler.features.Add(new ClearActionsNearBasesFeature { Radius = 8f });
    }

}
