using System.Collections.Generic;
using UnityEngine;

// Scatters small TileType.Mountain "obstacle" patches across the mid island (see
// MidIslandFeature) and every gem/waypoint/wedge-filler island (see GemIslandFeature.
// PlacedIslands, IslandBridgeFeature.WaypointIslands, WedgeIslandFeature.PlacedIslands) —
// deliberately NOT player base islands, which stay fully clear for base building/production.
// Mountain already blocks pathing the same way Air/Water do (see WorldManager's Tile
// Settings), so once stamped here every later walkability check — NavMeshHandler's own navmesh
// build, and every entity-cluster feature's IsWalkable check (IslandResourceClusterFeature,
// GemClusterFeature, SoulstoneClusterFeature, GoldCrateFeature) — automatically treats these
// tiles as unwalkable with no further changes needed anywhere else. Enqueue after
// WedgeIslandFeature/GemIslandFeature (every island + bridge this needs to know about must
// already exist) and before IslandResourceClusterFeature (so its trees/stone/ore scatter
// around the new obstacles instead of through them) — see WorldManager.SetupWorldGen.
//
// Patch shape/placement is driven by a single Perlin noise field (see PerlinNoiseGenerator)
// sampled in absolute world tile coordinates and offset so its own (0, 0) sits exactly on the
// mid island's center — always the exact map center (see MidIslandFeature) — rather than each
// island sampling its own independent, arbitrarily-offset patch of noise. The whole map's
// obstacle layout therefore reads as one coherent field anchored on the mid island, regardless
// of where any other island happens to land for a given seed/player count. A cell only ever
// becomes a candidate obstacle if the noise there clears NoiseThreshold for its island kind —
// MidIslandNoiseThreshold for the mid island, the considerably higher (so considerably rarer)
// OtherIslandNoiseThreshold for every other island — which is also what makes obstacles less
// frequent overall and sparser still on non-mid islands, without needing a separate density
// knob layered on top of the noise itself.
//
// Every candidate patch is rejected (never stamped) if it would either touch a bridge anchor's
// own buffer zone (severing that bridge from the island) or disconnect the island's remaining
// walkable area into more than one region (see WouldStayConnected) — so every island this
// touches is guaranteed to stay exactly as fully navigable as it was before, just with some of
// its open ground now impassable.
public class IslandObstacleFeature : WorldGenFeature
{
    // Perlin frequency, in world/tile units — roughly the wavelength (in tiles) of one noise
    // "lobe". Lower reads as bigger, smoother patches; higher as finer/more fragmented ones.
    public float NoiseScale = 0.07f;

    // Minimum noise value (see PerlinNoiseGenerator.Sample, range [0, 1]) a cell needs to seed
    // or join an obstacle patch. Only the mid island uses MidIslandNoiseThreshold; every other
    // target uses OtherIslandNoiseThreshold — set considerably higher so a much smaller
    // fraction of those islands' own footprints ever qualifies at all.
    public float MidIslandNoiseThreshold = 0.62f;
    public float OtherIslandNoiseThreshold = 0.76f;

    // A single noise-connected patch never grows past this many tiles, so one unusually large
    // contiguous high-noise region can't turn into one giant mountain range in a single patch.
    public int MaxPatchSizeTiles = 30;

    // Hard backstop, independent of the noise thresholds above: an island's cumulative
    // obstacle tiles never exceed this fraction of its original walkable footprint.
    public float MaxObstacleFraction = 0.12f;

    // Kept clear of every obstacle so a bridge's touchdown point (and the approach right
    // around it) never gets sealed off from the rest of the island.
    public float BridgeAnchorBufferRadius = 3f;

    // Kept clear of every already-spawned entity (soulstone nodes, gem deposits, gold crates,
    // ...) via SpawnedEntityRegistry, so a patch never visually swallows something already
    // placed there.
    public float EntityBufferRadius = 3f;

    private static readonly Vector2Int[] FloodFillDirections =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
    };

    public override void Generate(WorldGenHandler handler)
    {
        var midFeature = handler.GetPreviousFeature<MidIslandFeature>();
        IslandPlacementHelper.PlacedIsland? midIsland = midFeature?.MidIsland;

        var otherTargets = new List<IslandPlacementHelper.PlacedIsland>();

        var gemFeature = handler.GetPreviousFeature<GemIslandFeature>();
        if (gemFeature != null) otherTargets.AddRange(gemFeature.PlacedIslands);

        var bridgeFeature = handler.GetPreviousFeature<IslandBridgeFeature>();
        if (bridgeFeature != null) otherTargets.AddRange(bridgeFeature.WaypointIslands);

        var wedgeFeature = handler.GetPreviousFeature<WedgeIslandFeature>();
        if (wedgeFeature != null) otherTargets.AddRange(wedgeFeature.PlacedIslands);

        if (midIsland == null && otherTargets.Count == 0) return;

        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);

        // Anchored at the mid island's own center (or, in the pathological case it failed to
        // place, the map's own exact center — the same point MidIslandFeature always aims
        // for) so the noise field's (0, 0) always lines up with the mid island regardless of
        // whether that placement itself happened to succeed.
        Vector2 noiseOrigin = midIsland?.CenterWorldPosition ?? new Vector2(worldSize * 0.5f, worldSize * 0.5f);
        var noise = new PerlinNoiseGenerator("IslandObstacleFeature.Mountains")
        {
            Scale = NoiseScale,
            OffsetX = -noiseOrigin.x,
            OffsetY = -noiseOrigin.y,
        };

        var registry = handler.GetWorldGenResource<SpawnedEntityRegistry>(SpawnedEntityRegistry.ResourceKey);
        System.Random rng = handler.Random;

        if (midIsland.HasValue)
            ScatterObstacles(handler, midIsland.Value, registry, rng, worldSize, noise, MidIslandNoiseThreshold);

        foreach (IslandPlacementHelper.PlacedIsland island in otherTargets)
            ScatterObstacles(handler, island, registry, rng, worldSize, noise, OtherIslandNoiseThreshold);
    }

    private void ScatterObstacles(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland island, SpawnedEntityRegistry registry,
        System.Random rng, ushort worldSize, NoiseGenerator noise, float noiseThreshold)
    {
        // Mirrors StampFootprint's own edge-clipping: an island placed near the map boundary
        // can have RotatedFootprint cells that fall outside the actual world grid — those were
        // never stamped as TileType.Island in the first place, so they must never be treated
        // as walkable ground here either (SetTileType below would otherwise wrap a negative or
        // out-of-range coordinate into an unrelated tile via the ushort cast).
        var footprint = new HashSet<Vector2Int>();
        foreach (Vector2Int local in island.RotatedFootprint.OccupiedCells())
        {
            int tx = island.OriginX + local.x;
            int ty = island.OriginY + local.y;
            if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
            footprint.Add(new Vector2Int(tx, ty));
        }

        if (footprint.Count == 0) return;

        int maxObstacleTiles = Mathf.FloorToInt(footprint.Count * MaxObstacleFraction);
        if (maxObstacleTiles <= 0) return;

        HashSet<Vector2Int> excludedBuffer = ComputeExcludedBuffer(island, footprint, registry);
        HashSet<Vector2Int> obstacles = new HashSet<Vector2Int>();

        // Every footprint cell that could ever seed or join a patch — re-verified individually
        // during growth too (see GrowNoisePatch), since obstacles committed by an earlier
        // patch this same loop can only ever shrink what's still available.
        var candidateSeeds = new List<Vector2Int>();
        foreach (Vector2Int cell in footprint)
            if (!excludedBuffer.Contains(cell) && noise.Sample(cell.x, cell.y) >= noiseThreshold)
                candidateSeeds.Add(cell);

        if (candidateSeeds.Count == 0) return;

        // Shuffled so patches grow from scattered points across the island rather than always
        // in whatever arbitrary order the footprint HashSet happened to enumerate in.
        Shuffle(candidateSeeds, rng);

        foreach (Vector2Int seed in candidateSeeds)
        {
            if (obstacles.Count >= maxObstacleTiles) break;
            if (obstacles.Contains(seed) || excludedBuffer.Contains(seed)) continue;

            int remainingBudget = Mathf.Min(MaxPatchSizeTiles, maxObstacleTiles - obstacles.Count);
            List<Vector2Int> patch = GrowNoisePatch(footprint, obstacles, excludedBuffer, noise, noiseThreshold, seed, remainingBudget);
            if (patch.Count == 0) continue;
            if (!WouldStayConnected(footprint, obstacles, patch)) continue;

            foreach (Vector2Int cell in patch)
            {
                handler.SetTileType((ushort)cell.x, (ushort)cell.y, TileType.Mountain);
                obstacles.Add(cell);
            }
        }
    }

    // Flood-fills outward from seed through footprint cells whose noise also clears
    // noiseThreshold (an organic, noise-shaped blob rather than a hard-edged circle), stopping
    // once either the connected high-noise region runs out or maxSize tiles have been
    // collected. seed itself is assumed to already have been verified against the threshold
    // and every exclusion by the caller.
    private static List<Vector2Int> GrowNoisePatch(HashSet<Vector2Int> footprint, HashSet<Vector2Int> obstacles, HashSet<Vector2Int> excludedBuffer,
        NoiseGenerator noise, float noiseThreshold, Vector2Int seed, int maxSize)
    {
        var result = new List<Vector2Int>();
        if (maxSize <= 0) return result;

        var visited = new HashSet<Vector2Int> { seed };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(seed);

        while (queue.Count > 0 && result.Count < maxSize)
        {
            Vector2Int cell = queue.Dequeue();
            if (!footprint.Contains(cell) || obstacles.Contains(cell) || excludedBuffer.Contains(cell)) continue;
            if (noise.Sample(cell.x, cell.y) < noiseThreshold) continue;

            result.Add(cell);

            foreach (Vector2Int dir in FloodFillDirections)
            {
                Vector2Int next = cell + dir;
                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return result;
    }

    // True if the island's walkable area (footprint minus obstacles minus this candidate
    // patch) stays a single connected region under 4-directional adjacency — the same
    // adjacency NavMeshHandler.CreateNavMesh's rectangle merge actually connects through (a
    // diagonal-only touch between two blobs is NOT traversable), so this exactly matches what
    // "still fully navigable" means for the real pathfinding graph built from these tiles.
    private static bool WouldStayConnected(HashSet<Vector2Int> footprint, HashSet<Vector2Int> obstacles, List<Vector2Int> candidatePatch)
    {
        var blocked = new HashSet<Vector2Int>(obstacles);
        blocked.UnionWith(candidatePatch);

        Vector2Int seed = default;
        bool haveSeed = false;
        int remainingCount = 0;
        foreach (Vector2Int cell in footprint)
        {
            if (blocked.Contains(cell)) continue;
            remainingCount++;
            if (!haveSeed) { seed = cell; haveSeed = true; }
        }

        // Never allow a patch to swallow the island's entire remaining walkable area.
        if (!haveSeed) return false;

        var visited = new HashSet<Vector2Int> { seed };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int dir in FloodFillDirections)
            {
                Vector2Int next = current + dir;
                if (visited.Contains(next)) continue;
                if (!footprint.Contains(next) || blocked.Contains(next)) continue;
                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return visited.Count == remainingCount;
    }

    // Cells kept permanently clear of any obstacle patch: a buffer around every bridge anchor
    // (so a bridge's touchdown point never gets sealed off) and around every already-spawned
    // entity within/near this island (so a patch never visually swallows a soulstone/gem/gold
    // crate node already placed there).
    private HashSet<Vector2Int> ComputeExcludedBuffer(IslandPlacementHelper.PlacedIsland island, HashSet<Vector2Int> footprint, SpawnedEntityRegistry registry)
    {
        var excluded = new HashSet<Vector2Int>();

        foreach (Vector2Int localAnchor in island.RotatedFootprint.BridgeAnchors)
        {
            Vector2 anchorWorld = island.RotatedFootprint.AnchorWorldPosition(localAnchor, island.OriginX, island.OriginY);
            AddRadiusToExcluded(excluded, footprint, anchorWorld, BridgeAnchorBufferRadius);
        }

        if (registry != null)
        {
            Vector2 islandCenter = island.CenterWorldPosition;
            float searchRadius = Mathf.Max(island.RotatedFootprint.Width, island.RotatedFootprint.Height) * 0.75f + EntityBufferRadius;
            foreach (EntitySpawnAction action in registry.GetWithinRadius(islandCenter.x, islandCenter.y, searchRadius))
                AddRadiusToExcluded(excluded, footprint, new Vector2(action.X, action.Y), EntityBufferRadius);
        }

        return excluded;
    }

    private static void AddRadiusToExcluded(HashSet<Vector2Int> excluded, HashSet<Vector2Int> footprint, Vector2 center, float radius)
    {
        int r = Mathf.CeilToInt(radius);
        int cx = Mathf.RoundToInt(center.x), cy = Mathf.RoundToInt(center.y);
        float r2 = radius * radius;

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                Vector2Int cell = new Vector2Int(cx + dx, cy + dy);
                if (footprint.Contains(cell)) excluded.Add(cell);
            }
        }
    }

    private static void Shuffle(List<Vector2Int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
