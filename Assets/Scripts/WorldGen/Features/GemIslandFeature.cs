using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enqueue after IslandBridgeFeature and before WedgeIslandFeature (see
/// WorldManager.SetupWorldGen) — reads IslandPlayerBaseFeature.Islands and
/// MidIslandFeature.MidIsland via GetPreviousFeature, both of which must already be placed.
///
/// Places one small "gem island" per player base — resolved by name from
/// WorldManager.instance.IslandRegistry, like every other island type — using the same rotated-
/// bisector placement GemClusterFeature used to place its (island-less) clusters directly in
/// open space: take the line from each base to the exact map center, rotate the whole set of
/// lines by 360/(2n) degrees (n = base count) so each gem island's direction lands exactly
/// halfway between two adjacent base directions (clear of every spoke bridge), then place the
/// island OffsetFromCenter out along that rotated line, close to the mid island.
///
/// Each gem island is rotated (around the world's Y axis — see FootprintRotator) so its local
/// +Z axis always points back toward the mid island's center, then always gets bridged directly
/// to it (same "must connect" guarantee IslandBridgeFeature's own ring/spoke network uses —
/// falls back to an overlapping bridge rather than leaving one unconnected, since every gem
/// island is meant to be reachable), then gets one gem-deposit cluster on top of it, alternating
/// SmallClusterEntityCount/LargeClusterEntityCount the same way GemClusterFeature did.
///
/// Deliberately runs BEFORE WedgeIslandFeature: that feature's own footprint-overlap check
/// (WedgeIslandFeature.FootprintOverlapsExisting) scans actual stamped tiles rather than a
/// known-island list, so every gem island placed here is automatically treated as an obstacle
/// to route filler islands/bridges around once WedgeIslandFeature itself runs.
/// </summary>
public class GemIslandFeature : WorldGenFeature
{
    // Name looked up in WorldManager.instance.IslandRegistry for the gem island type.
    public string IslandName;

    // Bridge type looked up in WorldManager.instance.BridgeRegistry for connecting each gem
    // island to the mid island — see BridgeRegistry.
    public string BridgeTypeName;

    // Distance from the exact map center each gem island's own center is placed.
    public float OffsetFromCenter = 90f;

    // On overlap with something already placed (e.g. a larger mid island prefab than this was
    // tuned for), the radius grows by this much and retries rather than picking a new spot at
    // random — so a gem island always ends up just outside whatever's actually there instead of
    // failing outright.
    public float RadialStepOnOverlap = 6f;

    public float BowDistance = 0f;
    public float BridgePaddingTiles = 1f;

    public int SmallClusterEntityCount = 3;
    public int LargeClusterEntityCount = 7;
    public float ClusterRadius = 8f;
    public float MinEntitySpacing = 2f;

    // Radius (in tiles) of the Gravel patch painted around each individual gem deposit — see
    // GemClusterFeature.GravelRadius for the general idea; PaintGravelAround below additionally
    // guards against painting past the island's own edge (see that method's own comment).
    public float GravelRadius = 3f;

    private const int MaxOverlapRetries = 10;
    private const int MaxEntityPlacementAttempts = 30;

    private GameObject[] _bridgePrefabs;

    public override void Generate(WorldGenHandler handler)
    {
        var basesFeature = handler.GetPreviousFeature<IslandPlayerBaseFeature>();
        if (basesFeature == null || basesFeature.Islands.Count == 0) return;

        var midFeature = handler.GetPreviousFeature<MidIslandFeature>();
        if (midFeature == null || !midFeature.MidIsland.HasValue) return;

        IslandRegistry islandRegistry = WorldManager.instance != null ? WorldManager.instance.IslandRegistry : null;
        if (islandRegistry == null || !islandRegistry.TryGetPrefab(IslandName, out GameObject prefab))
        {
            Debug.LogWarning($"[GemIslandFeature] No island registered under name '{IslandName}' — skipping.");
            return;
        }

        IslandFootprint footprint = prefab != null ? prefab.GetComponent<IslandFootprint>() : null;
        if (footprint == null)
        {
            Debug.LogWarning($"[GemIslandFeature] Prefab '{(prefab != null ? prefab.name : "null")}' has no IslandFootprint component — skipping.");
            return;
        }

        _bridgePrefabs = ResolveBridgePrefabs();
        if (_bridgePrefabs == null || _bridgePrefabs.Length == 0)
        {
            Debug.LogWarning($"[GemIslandFeature] No bridge prefabs resolved for BridgeTypeName '{BridgeTypeName}' — skipping.");
            return;
        }

        IReadOnlyList<IslandPlayerBaseFeature.IslandPlacement> bases = basesFeature.Islands;
        IslandPlacementHelper.PlacedIsland mid = midFeature.MidIsland.Value;
        int n = bases.Count;
        float rotation = Mathf.PI / n;
        int smallClusterCount = n / 2; // rounded down, same split GemClusterFeature used

        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        Vector2 mapCenter = new Vector2(worldSize * 0.5f, worldSize * 0.5f);
        float maxCoord = worldSize - 1f;

        for (int i = 0; i < n; i++)
        {
            Vector2 baseCenter = bases[i].Island.CenterWorldPosition;
            float baseAngle = Mathf.Atan2(baseCenter.y - mapCenter.y, baseCenter.x - mapCenter.x);
            Vector2 direction = new Vector2(Mathf.Cos(baseAngle + rotation), Mathf.Sin(baseAngle + rotation));

            // The gem island's center is always mapCenter + direction * radius (see
            // PlaceClearOfMid), for whatever radius it actually settles on — so the direction
            // from ITS center back to the mid island's (== mapCenter, since MidIslandFeature
            // places it exactly there) is always exactly -direction, regardless of radius. No
            // need to recompute this per retry attempt.
            Vector2 towardMid = -direction;
            float facingDegrees = Quaternion.LookRotation(new Vector3(towardMid.x, 0f, towardMid.y)).eulerAngles.y;
            RotatedIslandFootprint rotatedFootprint = FootprintRotator.Rotate(footprint, facingDegrees);

            IslandPlacementHelper.PlacedIsland? placed = PlaceClearOfMid(handler, prefab, rotatedFootprint, facingDegrees, mapCenter, direction, worldSize);
            if (!placed.HasValue) continue;

            ConnectToMid(handler, placed.Value, mid, worldSize);

            int entityCount = i < smallClusterCount ? SmallClusterEntityCount : LargeClusterEntityCount;
            PlaceGemCluster(handler, placed.Value, entityCount, maxCoord);
        }
    }

    private GameObject[] ResolveBridgePrefabs()
    {
        BridgeRegistry registry = WorldManager.instance != null ? WorldManager.instance.BridgeRegistry : null;
        if (registry == null)
        {
            Debug.LogWarning("[GemIslandFeature] No BridgeRegistry assigned on WorldManager — skipping.");
            return null;
        }
        if (!registry.TryGetPrefabs(BridgeTypeName, out GameObject[] prefabs))
        {
            Debug.LogWarning($"[GemIslandFeature] No bridge type registered under name '{BridgeTypeName}'.");
            return null;
        }
        return prefabs;
    }

    // Walks the candidate center outward along direction (starting at OffsetFromCenter, growing
    // by RadialStepOnOverlap on every overlap) until rotatedFootprint clears everything already
    // stamped, then places it there at rotationDegrees. Always places something within
    // MaxOverlapRetries attempts — a gem island is meant to always exist for every base, same
    // "always builds something" guarantee as IslandBridgeFeature's own ring/spoke network,
    // unlike WedgeIslandFeature's best-effort filler islands. rotatedFootprint is resolved once
    // by the caller (facingDegrees doesn't change as radius grows — see Generate) rather than
    // recomputed on every retry.
    private IslandPlacementHelper.PlacedIsland? PlaceClearOfMid(WorldGenHandler handler, GameObject prefab, RotatedIslandFootprint rotatedFootprint,
        float rotationDegrees, Vector2 mapCenter, Vector2 direction, ushort worldSize)
    {
        float radius = OffsetFromCenter;
        int centerTileX = 0, centerTileY = 0;

        for (int attempt = 0; attempt < MaxOverlapRetries; attempt++)
        {
            Vector2 centerF = mapCenter + direction * radius;
            centerTileX = Mathf.Clamp(Mathf.RoundToInt(centerF.x), 0, worldSize - 1);
            centerTileY = Mathf.Clamp(Mathf.RoundToInt(centerF.y), 0, worldSize - 1);

            int originX = centerTileX - rotatedFootprint.Width / 2;
            int originY = centerTileY - rotatedFootprint.Height / 2;

            if (!FootprintOverlapsExisting(handler, rotatedFootprint, originX, originY, worldSize))
                return IslandPlacementHelper.TryPlaceIsland(handler, prefab, centerTileX, centerTileY, worldSize, rotationDegrees);

            radius += RadialStepOnOverlap;
        }

        return IslandPlacementHelper.TryPlaceIsland(handler, prefab, centerTileX, centerTileY, worldSize, rotationDegrees);
    }

    // Same "is any occupied cell already something other than Air" check WedgeIslandFeature uses
    // for its own filler islands — duplicated rather than shared, same reasoning GemClusterFeature
    // gives for duplicating EntityClusterFeature's own placement helpers. Takes the already-
    // rotated footprint (not the source IslandFootprint) so the overlap check tests the exact
    // shape that will actually be stamped, not its as-authored orientation.
    private static bool FootprintOverlapsExisting(WorldGenHandler handler, RotatedIslandFootprint footprint, int originX, int originY, ushort worldSize)
    {
        foreach (Vector2Int cell in footprint.OccupiedCells())
        {
            int tx = originX + cell.x;
            int ty = originY + cell.y;
            if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
            if (handler.GetTileType((ushort)tx, (ushort)ty) != TileType.Air) return true;
        }
        return false;
    }

    private void ConnectToMid(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland island, IslandPlacementHelper.PlacedIsland mid, ushort worldSize)
    {
        GameObject bridgePrefab = _bridgePrefabs[handler.Random.Next(_bridgePrefabs.Length)];
        BridgeSegmentFootprint segmentInfo = bridgePrefab != null ? bridgePrefab.GetComponent<BridgeSegmentFootprint>() : null;
        if (segmentInfo == null)
        {
            Debug.LogWarning($"[GemIslandFeature] Prefab '{(bridgePrefab != null ? bridgePrefab.name : "null")}' has no BridgeSegmentFootprint component — skipping this connection.");
            return;
        }

        // allowOverlapFallback: true — a gem island is meant to always be reachable, same
        // reasoning as IslandBridgeFeature.BuildSingleConnection.
        if (!BridgeConnectionBuilder.TryFindConnection(handler, island, mid, BowDistance, segmentInfo.SegmentWidth, BridgePaddingTiles, worldSize,
                allowOverlapFallback: true, out BridgeConnectionBuilder.BridgeCandidate chosen))
            return;

        BridgeConnectionBuilder.Commit(handler, chosen, bridgePrefab, segmentInfo, BridgePaddingTiles, worldSize);
    }

    private void PlaceGemCluster(WorldGenHandler handler, IslandPlacementHelper.PlacedIsland island, int entityCount, float maxCoord)
    {
        var rng = handler.Random;
        Vector2 center = island.CenterWorldPosition;
        var placed = new List<(float x, float y)>(entityCount);

        for (int e = 0; e < entityCount; e++)
        {
            if (!TryPickEntityPosition(center.x, center.y, rng, maxCoord, placed, out float x, out float y))
                continue;

            placed.Add((x, y));
            PaintGravelAround(handler, x, y, maxCoord);
            handler.EnqueueAction(new EntitySpawnAction { X = x, Y = y, Spawner = EntitySpawnAction.SpawnGemDeposit });
        }
    }

    // Same disc-fill idea as GemClusterFeature.PaintGravelAround, but guarded to only repaint
    // tiles the island itself already occupies — GemClusterFeature could paint freely because its
    // whole map was solid ground; a gem island here is a small patch of Island tiles surrounded
    // by Air, so painting indiscriminately would bite a strip of "floating" Gravel out over open
    // space whenever a deposit landed near the island's edge.
    private void PaintGravelAround(WorldGenHandler handler, float x, float y, float maxCoord)
    {
        int centerX = Mathf.RoundToInt(x);
        int centerY = Mathf.RoundToInt(y);
        int radius = Mathf.CeilToInt(GravelRadius);
        float radius2 = GravelRadius * GravelRadius;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius2) continue;

                int px = centerX + dx;
                int py = centerY + dy;
                if (px < 0 || py < 0 || px > maxCoord || py > maxCoord) continue;
                if (handler.GetTileType((ushort)px, (ushort)py) != TileType.Island) continue;

                handler.SetTileType((ushort)px, (ushort)py, TileType.Gravel);
            }
        }
    }

    private bool TryPickEntityPosition(float centerX, float centerY, System.Random rng, float maxCoord, List<(float x, float y)> placed, out float x, out float y)
    {
        for (int attempt = 0; attempt < MaxEntityPlacementAttempts; attempt++)
        {
            float angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
            float dist  = (float)(rng.NextDouble() * ClusterRadius);

            float candidateX = Mathf.Clamp(centerX + Mathf.Cos(angle) * dist, 0f, maxCoord);
            float candidateY = Mathf.Clamp(centerY + Mathf.Sin(angle) * dist, 0f, maxCoord);

            if (!IsWalkable(candidateX, candidateY)) continue;
            if (MinEntitySpacing <= 0f || IsFarEnoughFromAll(candidateX, candidateY, placed))
            {
                x = candidateX;
                y = candidateY;
                return true;
            }
        }
        x = y = 0f;
        return false;
    }

    private bool IsFarEnoughFromAll(float x, float y, List<(float x, float y)> placed)
    {
        float minDist2 = MinEntitySpacing * MinEntitySpacing;
        foreach (var p in placed)
        {
            float dx = p.x - x, dy = p.y - y;
            if (dx * dx + dy * dy < minDist2) return false;
        }
        return true;
    }

    private static bool IsWalkable(float x, float y)
    {
        if (WorldManager.instance == null) return true;
        ushort tileX = (ushort)Mathf.Max(0, Mathf.RoundToInt(x));
        ushort tileY = (ushort)Mathf.Max(0, Mathf.RoundToInt(y));
        return !WorldManager.instance.HasCollision(tileX, tileY);
    }
}
