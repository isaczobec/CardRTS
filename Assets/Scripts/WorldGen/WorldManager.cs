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

    // Maps island type names to prefabs (see IslandRegistry) — WorldGenFeatures that place
    // islands (e.g. IslandPlayerBaseFeature) request one by name against this rather than
    // holding prefab references themselves.
    [SerializeField] IslandRegistry _islandRegistry;
    public IslandRegistry IslandRegistry => _islandRegistry;

    // Which IslandRegistry entries are eligible to be picked for a player base — one is
    // picked at random (deterministically, via handler.Random) per player. See
    // IslandPlayerBaseFeature.IslandNames.
    [SerializeField] string[] _playerBaseIslandNames;

    // How far in from the world edge the ring of islands is inscribed — see
    // IslandPlayerBaseFeature.EdgeOffset. Needs to comfortably clear the largest configured
    // island's half-width/height so its footprint never gets clipped against the world border.
    [SerializeField] float _islandEdgeOffset = 40f;

    // IslandRegistry name for the single neutral island placed at the exact map center — see
    // MidIslandFeature.IslandName.
    [SerializeField] string _midIslandName;

    // IslandRegistry name for the small gem-deposit islands placed close to the mid island, one
    // per player base — see GemIslandFeature.IslandName.
    [SerializeField] string _gemIslandName;

    // Maps bridge type names to pools of segment prefabs (see BridgeRegistry) — features that
    // build bridges (IslandBridgeFeature, WedgeIslandFeature) request a type by name against
    // this rather than holding prefab references themselves, so different kinds of connection
    // can look visually distinct (see _mainBridgeTypeName / _wedgeBridgeTypeName below).
    [SerializeField] BridgeRegistry _bridgeRegistry;
    public BridgeRegistry BridgeRegistry => _bridgeRegistry;

    // Bridge type for every ring/spoke connection in the main network — see
    // IslandBridgeFeature.BridgeTypeName.
    [SerializeField] string _mainBridgeTypeName;

    // Bridge type for wedge-filler-island connections — deliberately separate from
    // _mainBridgeTypeName so the two kinds of connection can read as visually distinct — see
    // WedgeIslandFeature.BridgeTypeName.
    [SerializeField] string _wedgeBridgeTypeName;

    // How far a single ring hop's curve bows away from a straight line between its two
    // anchor points — see IslandBridgeFeature.BowDistance.
    [SerializeField] float _bridgeBowDistance = 6f;

    // Bow for spoke (base-to-mid-island) connections — 0 makes them completely straight.
    // See IslandBridgeFeature.SpokeBowDistance.
    [SerializeField] float _spokeBowDistance = 0f;

    // IslandRegistry names for waypoint islands threaded along a bridge — see
    // IslandBridgeFeature.IntermittentIslandNames.
    [SerializeField] string[] _intermittentIslandNames;

    // How many waypoint islands sit along each base-to-base ring connection / each
    // base-to-mid-island spoke connection — see IslandBridgeFeature.RingIntermittentCount /
    // SpokeIntermittentCount.
    [SerializeField] int _ringIntermittentCount = 3;
    // Two intermittent islands per spoke (base-to-mid) connection — explicit design ask.
    [SerializeField] int _spokeIntermittentCount = 2;

    // See IslandBridgeFeature.RingBowEdgeMargin / RingBowChordMultiplier.
    [SerializeField] float _ringBowEdgeMargin = 10f;
    [SerializeField] float _ringBowChordMultiplier = 1.5f;

    // How far past each anchor point a bridge's stamped tiles overshoot, guaranteeing they
    // connect to the island even if a sample lands exactly on a tile boundary — see
    // IslandBridgeFeature.BridgePaddingTiles.
    [SerializeField] float _bridgePaddingTiles = 1f;

    // (Name, Weight) pairs WedgeIslandFeature picks filler islands from — see
    // WedgeIslandFeature.IslandWeights.
    [SerializeField] WeightedIslandEntry[] _wedgeIslandWeights;

    // How many filler islands WedgeIslandFeature places in each wedge-shaped gap between the
    // mid and side bridges — see WedgeIslandFeature.MinIslandsPerWedge / MaxIslandsPerWedge.
    // Reduced from 1/2 — explicit design ask.
    [SerializeField] int _minIslandsPerWedge = 0;
    [SerializeField] int _maxIslandsPerWedge = 1;

    // See WedgeIslandFeature.WedgeAngularInset / WedgeRadialMargin.
    [SerializeField] float _wedgeAngularInset = 0.12f;
    [SerializeField] float _wedgeRadialMargin = 8f;

    // See WedgeIslandFeature.MaxConnectionDistance.
    [SerializeField] float _wedgeMaxConnectionDistance = 50f;

    // See WedgeIslandFeature.ExtraConnectionChance / MaxExtraConnections.
    [SerializeField] float _wedgeExtraConnectionChance = 0.3f;
    [SerializeField] int _wedgeMaxExtraConnections = 2;

    // See WedgeIslandFeature.CenterBiasSamples.
    [SerializeField] int _wedgeCenterBiasSamples = 3;

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
        NavMeshHandler.instance.CreateNavMesh(this, Handler.IslandGraph);
        Renderer.Render(Handler);

        // Render-side only (see WorldChunkVisibilityManager's own doc comment) — safe to run
        // unconditionally on every machine, same as Renderer.Render/GoTileManager above. Not
        // present in every scene (e.g. tests), so this is opt-in via the singleton existing.
        WorldChunkVisibilityManager.instance?.Initialize();

        // Every peer executes every action (unlike Generate/Render, which were always
        // unconditional). Actions that touch shared ECS state — e.g. EntitySpawnAction —
        // are responsible for gating themselves to the server internally (that entity is
        // included in the initial ECS snapshot sent to clients, so it propagates over the
        // network instead); purely cosmetic actions like SpawnMeshPatchAction have no such
        // gate and just run locally on every machine, same as Renderer.Render/GoTileManager.
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

        // --- Floating islands, work in progress ---
        // Void-fill the whole world, then place one island + player base per connected
        // client, evenly spaced around the map like SpawnPlayerBasesFeature used to place
        // bases alone. Soulstones/gems/tree-stone-ore clusters below are the floating-island-
        // native replacements for the old (still-present-but-unreachable) biome-based versions
        // further down — everything from the unreachable `handler.features.Add(new
        // SpawnPlayerBasesFeature())` onward assumed a fully-painted terrain grid and will need
        // reworking (or replacing) for a mostly-Air world with sparse walkable islands.
        handler.features.Add(new FillWorldFeature { FillType = TileType.Air });
        handler.features.Add(new IslandPlayerBaseFeature
        {
            IslandNames = _playerBaseIslandNames,
            EdgeOffset = _islandEdgeOffset,
        });
        handler.features.Add(new MidIslandFeature
        {
            IslandName = _midIslandName,
        });

        // Neutral soulstone objective cluster, in a ring on the mid island — only needs the mid
        // island's tiles to already exist, so it runs right after MidIslandFeature regardless of
        // the bridge/wedge network built below.
        handler.features.Add(new SoulstoneClusterFeature());

        // Neutral gold crates scattered across the mid island, scaled by player count — after
        // SoulstoneClusterFeature so crates spawn clear of its three nodes (see
        // GoldCrateFeature.MinDistanceToOtherEntities).
        handler.features.Add(new GoldCrateFeature());

        handler.features.Add(new IslandBridgeFeature
        {
            BridgeTypeName = _mainBridgeTypeName,
            BowDistance = _bridgeBowDistance,
            SpokeBowDistance = _spokeBowDistance,
            IntermittentIslandNames = _intermittentIslandNames,
            // No more base-to-base "side" bridges — explicit design ask ("remove the side
            // bridges between the islands of the players"). Every base now only connects to
            // others by routing through the mid island via its own spoke.
            BuildRingConnections = false,
            RingIntermittentCount = _ringIntermittentCount,
            SpokeIntermittentCount = _spokeIntermittentCount,
            RingBowEdgeMargin = _ringBowEdgeMargin,
            RingBowChordMultiplier = _ringBowChordMultiplier,
            BridgePaddingTiles = _bridgePaddingTiles,
        });

        // Neutral capturable objective building on every ring/spoke waypoint island placed
        // above — see CapturableBuildingFeature/CapturableBuildingSystem.
        handler.features.Add(new CapturableBuildingFeature());

        // One gem-deposit island per base, close to the mid island and bridged to it — must run
        // after IslandBridgeFeature (so the guaranteed ring/spoke network claims its space
        // first) and before WedgeIslandFeature (so wedge filler islands correctly route around
        // the gem islands placed here — see GemIslandFeature's own doc comment).
        handler.features.Add(new GemIslandFeature
        {
            IslandName = _gemIslandName,
            BridgeTypeName = _wedgeBridgeTypeName,
            BridgePaddingTiles = _bridgePaddingTiles,
        });

        handler.features.Add(new WedgeIslandFeature
        {
            IslandWeights = _wedgeIslandWeights,
            MinIslandsPerWedge = _minIslandsPerWedge,
            MaxIslandsPerWedge = _maxIslandsPerWedge,
            BridgeTypeName = _wedgeBridgeTypeName,
            BowDistance = _bridgeBowDistance,
            BridgePaddingTiles = _bridgePaddingTiles,
            WedgeAngularInset = _wedgeAngularInset,
            WedgeRadialMargin = _wedgeRadialMargin,
            MaxConnectionDistance = _wedgeMaxConnectionDistance,
            ExtraConnectionChance = _wedgeExtraConnectionChance,
            MaxExtraConnections = _wedgeMaxExtraConnections,
            CenterBiasSamples = _wedgeCenterBiasSamples,
        });

        // Mountain obstacle patches scattered across the mid island and every gem/waypoint/
        // wedge island placed above (never player bases) — after every island/bridge this
        // needs to know about already exists, and before IslandResourceClusterFeature below so
        // its tree/stone/ore clusters scatter around the new obstacles instead of through them.
        handler.features.Add(new IslandObstacleFeature());

        // Tree/stone/ore clusters scattered across every ring/spoke waypoint island and wedge
        // filler island placed above — see IslandResourceClusterFeature's own doc comment for
        // why player bases, the mid island, and the gem islands above are excluded.
        handler.features.Add(new IslandResourceClusterFeature());

        return;
#pragma warning disable CS0162 // unreachable code below — kept intact to restore later
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
                    // ClusterPatchId        = "MesaPatch",
                    // ClusterPatchSize      = 20f,
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
                    // ClusterPatchId        = "MossPatch",
                    // ClusterPatchSize      = 45f,
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
                    // ClusterPatchId        = "MesaPatch",
                    // ClusterPatchSize      = 20f,
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
                    // ClusterPatchId        = "MossPatch",
                    // ClusterPatchSize      = 45f,
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
                    // ClusterPatchId        = "GravelPatch",
                    // ClusterPatchSize      = 15f,
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
                    // ClusterPatchId        = "GravelPatch",
                    // ClusterPatchSize      = 15f,
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
                    // ClusterPatchId        = "GravelPatch",
                    // ClusterPatchSize      = 15f,
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
                    // ClusterPatchId        = "GravelPatch",
                    // ClusterPatchSize      = 15f,
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
#pragma warning restore CS0162
    }

}
