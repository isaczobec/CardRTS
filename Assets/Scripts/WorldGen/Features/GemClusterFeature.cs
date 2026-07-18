using System;
using System.Collections.Generic;
using UnityEngine;

// Places one gem-deposit cluster (see EntitySpawnAction.SpawnGemDeposit) per player base,
// positioned as follows: take the line from each base to the exact map center, rotate the
// whole set of lines by 360/(2n) degrees (n = base count) — landing each cluster direction
// exactly halfway between two adjacent base directions — then place a cluster at
// ClusterOffsetFromCenter along each rotated line. Half of the clusters (rounded down, by
// index order after rotation) get SmallClusterEntityCount objects, the rest get
// LargeClusterEntityCount.
//
// Requires SpawnPlayerBasesFeature to have already run (see WorldManager.SetupWorldGen) —
// reads its Bases list via GetPreviousFeature, mirroring RemapTilesNearPointsFeature.
public class GemClusterFeature : WorldGenFeature
{
    // Distance from the exact map center each cluster's own center is placed.
    public float ClusterOffsetFromCenter = 80f;

    // How far individual objects within a cluster scatter around that cluster's center.
    public float ClusterRadius = 6f;

    // Minimum distance enforced between objects placed within the same cluster.
    public float MinEntitySpacing = 2f;

    public int SmallClusterEntityCount = 3;
    public int LargeClusterEntityCount = 7;

    private const int MaxPlacementAttempts = 30;

    public override void Generate(WorldGenHandler handler)
    {
        var basesFeature = handler.GetPreviousFeature<SpawnPlayerBasesFeature>();
        if (basesFeature == null || basesFeature.Bases.Count == 0) return;

        int n = basesFeature.Bases.Count;
        float worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
        float center = worldSize * 0.5f;
        float maxCoord = worldSize - 1f;

        // 360 / (2n) degrees, in radians.
        float rotation = Mathf.PI / n;

        int smallClusterCount = n / 2; // rounded down

        for (int i = 0; i < n; i++)
        {
            SpawnPlayerBasesFeature.PlayerBase b = basesFeature.Bases[i];
            float baseAngle = Mathf.Atan2(b.Y - center, b.X - center);
            float clusterAngle = baseAngle + rotation;

            float clusterCenterX = center + Mathf.Cos(clusterAngle) * ClusterOffsetFromCenter;
            float clusterCenterY = center + Mathf.Sin(clusterAngle) * ClusterOffsetFromCenter;

            int entityCount = i < smallClusterCount ? SmallClusterEntityCount : LargeClusterEntityCount;
            PlaceCluster(handler, clusterCenterX, clusterCenterY, entityCount, maxCoord);
        }
    }

    private void PlaceCluster(WorldGenHandler handler, float centerX, float centerY, int entityCount, float maxCoord)
    {
        var rng = handler.Random;
        var placed = new List<(float x, float y)>(entityCount);

        for (int e = 0; e < entityCount; e++)
        {
            if (!TryPickEntityPosition(centerX, centerY, rng, maxCoord, placed, out float x, out float y))
                continue;

            placed.Add((x, y));
            handler.EnqueueAction(new EntitySpawnAction { X = x, Y = y, Spawner = EntitySpawnAction.SpawnGemDeposit });
        }
    }

    // Same "random point within ClusterRadius of the center, retried until far enough from
    // every already-placed point in this cluster" approach as EntityClusterFeature's own
    // TryPickEntityPosition — duplicated rather than shared since this feature's cluster
    // centers are computed directly (rotated base lines), not picked from valid biome tiles.
    private bool TryPickEntityPosition(float centerX, float centerY, System.Random rng, float maxCoord, List<(float x, float y)> placed, out float x, out float y)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            float angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
            float dist  = (float)(rng.NextDouble() * ClusterRadius);

            float candidateX = Mathf.Clamp(centerX + Mathf.Cos(angle) * dist, 0f, maxCoord);
            float candidateY = Mathf.Clamp(centerY + Mathf.Sin(angle) * dist, 0f, maxCoord);

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
}
