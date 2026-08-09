using System;
using System.Collections.Generic;
using UnityEngine;

// Scatters clusters of the three basic resources (trees, stone, ore/metal) across every
// "filler" island in the world — the waypoint islands IslandBridgeFeature threads along each
// ring (base-to-base) and spoke (base-to-mid) bridge connection, plus the extra filler islands
// WedgeIslandFeature scatters into the pockets between the mid and side bridges. Player base
// islands, the mid island, and GemIslandFeature's own dedicated gem islands are deliberately
// left alone — they already have their own base/objective/gem content.
//
// Enqueue after both IslandBridgeFeature and WedgeIslandFeature (see WorldManager.SetupWorldGen)
// — reads IslandBridgeFeature.WaypointIslands and WedgeIslandFeature.PlacedIslands via
// GetPreviousFeature, both of which must already be placed.
//
// Per island, the number of clusters is a probabilistic rounding of (the island's own walkable
// tile count * ClustersPerTile) — see RollClusterCount — so a big island averages more clusters
// than a small one without either ever being guaranteed a hard-coded count. Each cluster's
// resource is picked with odds weighted inversely to how much of that resource has already been
// placed so far this generation (see PickBalancedResource) — not a flat 1/3 each, and not a
// strict round robin either, so the map-wide totals stay roughly even across Tree/Stone/Ore
// without ever being forced into exact equality or ever fully losing the randomness.
public class IslandResourceClusterFeature : WorldGenFeature
{
    // Expected clusters per walkable tile — e.g. 0.001 means a 1000-tile island averages one
    // cluster. See RollClusterCount for how this becomes a whole-number roll per island.
    public float ClustersPerTile = 0.0009f;

    public int EntitiesPerClusterMin = 3;
    public int EntitiesPerClusterMax = 6;
    public float ClusterRadius = 7f;
    public float MinEntitySpacing = 2.5f;

    private const int MaxPlacementAttempts = 30;

    private static readonly Action<ulong, ECS>[] ResourceSpawners =
    {
        EntitySpawnAction.SpawnTree,
        EntitySpawnAction.SpawnRock,
        EntitySpawnAction.SpawnOre,
    };

    public override void Generate(WorldGenHandler handler)
    {
        var bridgeFeature = handler.GetPreviousFeature<IslandBridgeFeature>();
        var wedgeFeature = handler.GetPreviousFeature<WedgeIslandFeature>();

        var targets = new List<IslandPlacementHelper.PlacedIsland>();
        if (bridgeFeature != null) targets.AddRange(bridgeFeature.WaypointIslands);
        if (wedgeFeature != null) targets.AddRange(wedgeFeature.PlacedIslands);
        if (targets.Count == 0) return;

        System.Random rng = handler.Random;
        float maxCoord = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1f;

        // Running entity totals per resource type, across every island this Generate() call
        // places clusters on — read/updated by PickBalancedResource so the map-wide mix stays
        // roughly even regardless of how many clusters land on any one island.
        var resourceTotals = new int[ResourceSpawners.Length];

        foreach (IslandPlacementHelper.PlacedIsland island in targets)
        {
            var occupiedCells = new List<Vector2Int>(island.RotatedFootprint.OccupiedCells());
            if (occupiedCells.Count == 0) continue;

            int clusterCount = RollClusterCount(occupiedCells.Count, rng);
            for (int c = 0; c < clusterCount; c++)
            {
                Vector2Int cell = occupiedCells[rng.Next(occupiedCells.Count)];
                float centerX = island.OriginX + cell.x + 0.5f;
                float centerY = island.OriginY + cell.y + 0.5f;

                int entityCount = rng.Next(EntitiesPerClusterMin, EntitiesPerClusterMax + 1);
                int resourceIndex = PickBalancedResource(resourceTotals, rng);
                resourceTotals[resourceIndex] += entityCount;

                PlaceCluster(handler, centerX, centerY, entityCount, ResourceSpawners[resourceIndex], maxCoord);
            }
        }
    }

    // Weighted pick where each resource's weight is 1 / (its running total + 1) — the same
    // "roll then subtract weight until it lands" approach WedgeIslandFeature.PickWeighted
    // already uses, just with weights recomputed from resourceTotals every call instead of a
    // fixed list. A resource sitting at 0 while the others are well ahead gets picked far more
    // often (weight close to 1 vs. a fraction for anything already common); once every count is
    // roughly equal the weights converge back toward flat 1/3 odds — self-correcting without
    // ever being a hard quota.
    private static int PickBalancedResource(int[] resourceTotals, System.Random rng)
    {
        float total = 0f;
        var weights = new float[resourceTotals.Length];
        for (int i = 0; i < resourceTotals.Length; i++)
        {
            weights[i] = 1f / (resourceTotals[i] + 1);
            total += weights[i];
        }

        float roll = (float)(rng.NextDouble() * total);
        for (int i = 0; i < weights.Length; i++)
        {
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return weights.Length - 1;
    }

    // Floors the mean and then rolls one more with probability equal to its fractional part, so
    // the expected count across many islands/games matches tileCount * ClustersPerTile exactly
    // even though any single island only ever gets a whole number (0 included, for small islands
    // whose mean is well under 1).
    private int RollClusterCount(int tileCount, System.Random rng)
    {
        float mean = tileCount * ClustersPerTile;
        int count = (int)mean;
        float frac = mean - count;
        if (rng.NextDouble() < frac) count++;
        return count;
    }

    private void PlaceCluster(WorldGenHandler handler, float centerX, float centerY, int entityCount, Action<ulong, ECS> spawner, float maxCoord)
    {
        System.Random rng = handler.Random;
        var placed = new List<(float x, float y)>(entityCount);

        for (int e = 0; e < entityCount; e++)
        {
            if (!TryPickEntityPosition(centerX, centerY, rng, maxCoord, placed, out float x, out float y))
                continue;

            placed.Add((x, y));
            handler.EnqueueAction(new EntitySpawnAction { X = x, Y = y, Spawner = spawner });
        }
    }

    // Same "random point within ClusterRadius, retried until walkable and far enough from every
    // already-placed point in this cluster" approach EntityClusterFeature/GemClusterFeature both
    // use — duplicated rather than shared since this feature's cluster centers come from an
    // island's own footprint cells, not a biome's noise-sampled tile list or a computed rotated
    // point.
    private bool TryPickEntityPosition(float centerX, float centerY, System.Random rng, float maxCoord, List<(float x, float y)> placed, out float x, out float y)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
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

    // Same walkability concept as EntityClusterFeature.IsWalkable — see that method's own doc
    // comment for why this is safe mid-world-gen.
    private static bool IsWalkable(float x, float y)
    {
        if (WorldManager.instance == null) return true;
        ushort tileX = (ushort)Mathf.Max(0, Mathf.RoundToInt(x));
        ushort tileY = (ushort)Mathf.Max(0, Mathf.RoundToInt(y));
        return !WorldManager.instance.HasCollision(tileX, tileY);
    }
}
