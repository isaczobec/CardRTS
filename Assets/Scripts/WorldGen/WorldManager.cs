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

        // --- Terrain noise ---
        handler.AddResource(new PerlinNoiseGenerator("terrain")
        {
            Scale   = 0.04f,
            OffsetX = 0f,
            OffsetY = 0f,
        });

        // --- Biome axes: temperature (X) and humidity (Y) ---
        handler.AddResource(new PerlinNoiseGenerator("biomeTemp")
        {
            Scale   = 0.005f,
            OffsetX = 500f,
            OffsetY = 300f,
        });
        handler.AddResource(new PerlinNoiseGenerator("biomeHumidity")
        {
            Scale   = 0.005f,
            OffsetX = 200f,
            OffsetY = 700f,
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
            }
        });

        // Wetland biome: cold (low temp) and humid (high humidity).
        handler.features.Add(new BiomeFeature
        {
            NoiseKeyX = "biomeTemp",
            NoiseKeyY = "biomeHumidity",
            MinX = 0.00f, MaxX = 0.60f,
            MinY = 0.00f, MaxY = 1.00f,
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
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnTree,
                    ClusterCountMin       = 25,
                    ClusterCountMax       = 35,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 4,
                    ClusterRadius         = 15f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 6f,
                    AllowedTileTypes      = new[] { TileType.Grass },
                },
                new EntityClusterFeature
                {
                    Spawner               = EntitySpawnAction.SpawnRock,
                    ClusterCountMin       = 25,
                    ClusterCountMax       = 35,
                    EntitiesPerClusterMin = 3,
                    EntitiesPerClusterMax = 5,
                    ClusterRadius         = 5f,
                    MinDistanceToOtherEntities = 20f,
                    MinEntitySpacing = 1.5f,
                    AllowedTileTypes      = new[] { TileType.Grass },
                
                },
            }
        });

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
