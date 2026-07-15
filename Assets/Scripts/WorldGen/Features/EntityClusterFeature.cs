using System;
using System.Collections.Generic;
using UnityEngine;

// Places entity clusters within the parent biome (or across the whole world if there is
// no parent BiomeFeature). Set Spawner to any EntitySpawnAction.SpawnXxx delegate to
// control what gets placed; the number of clusters and the number of entities per cluster
// are each drawn uniformly from their Min/Max range (once per feature for the cluster
// count, once per cluster for the entity count) via handler.Random, so it stays
// deterministic across server/client.
//
// Usage — as a biome child:
//   new EntityClusterFeature
//   {
//       Spawner               = EntitySpawnAction.SpawnTree,
//       ClusterCountMin       = 4,
//       ClusterCountMax       = 4,
//       EntitiesPerClusterMin = 6,
//       EntitiesPerClusterMax = 6,
//   }
public class EntityClusterFeature : WorldGenFeature
{
    public Action<ulong, ECS> Spawner;
    public int ClusterCountMin = 5;
    public int ClusterCountMax = 5;
    public int EntitiesPerClusterMin = 8;
    public int EntitiesPerClusterMax = 8;
    public float ClusterRadius = 5f;
    public TileType[] AllowedTileTypes = { TileType.Grass };

    // A cluster's center is re-rolled (up to MaxPlacementAttempts times) if any
    // already-spawned entity — from an earlier feature, or an earlier cluster in this same
    // feature — is closer than this. 0 disables the check. Looked up via the
    // SpawnedEntityRegistry world gen resource, so this only has an effect if one has been
    // added (see WorldManager.SetupWorldGen); if none was added, the check is skipped.
    public float MinDistanceToOtherEntities = 0f;

    // Minimum distance enforced between entities placed within the same cluster, so e.g.
    // trees don't overlap each other. 0 disables the check.
    public float MinEntitySpacing = 0f;

    // Cap on retries when a candidate position fails a minimum-distance check, so a dense
    // area can't hang generation — clusters/entities that can't find a valid spot are
    // skipped instead (see TryPickClusterCenter/TryPickEntityPosition).
    private const int MaxPlacementAttempts = 30;

    public override void Generate(WorldGenHandler handler)
    {
        var rng = handler.Random;
        var biome = handler.GetPreviousFeature<BiomeFeature>();
        var registry = handler.GetWorldGenResource<SpawnedEntityRegistry>(SpawnedEntityRegistry.ResourceKey);

        List<(ushort x, ushort y)> validTiles = GatherValidTiles(handler, biome);
        if (validTiles.Count == 0) return;

        float maxCoord = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1f;

        int clusterCount = rng.Next(ClusterCountMin, ClusterCountMax + 1);
        for (int c = 0; c < clusterCount; c++)
        {
            if (!TryPickClusterCenter(validTiles, rng, registry, out float centerX, out float centerY))
                continue;

            int entitiesPerCluster = rng.Next(EntitiesPerClusterMin, EntitiesPerClusterMax + 1);
            var placed = new List<(float x, float y)>(entitiesPerCluster);

            for (int e = 0; e < entitiesPerCluster; e++)
            {
                if (!TryPickEntityPosition(centerX, centerY, rng, maxCoord, placed, out float x, out float y))
                    continue;

                placed.Add((x, y));
                handler.EnqueueAction(new EntitySpawnAction { X = x, Y = y, Spawner = Spawner });
            }
        }
    }

    private bool TryPickClusterCenter(List<(ushort x, ushort y)> validTiles, System.Random rng, SpawnedEntityRegistry registry, out float centerX, out float centerY)
    {
        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            (ushort tx, ushort ty) = validTiles[rng.Next(validTiles.Count)];
            if (MinDistanceToOtherEntities <= 0f || registry == null || !registry.AnyWithinRadius(tx, ty, MinDistanceToOtherEntities))
            {
                centerX = tx;
                centerY = ty;
                return true;
            }
        }
        centerX = centerY = 0f;
        return false;
    }

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

    private List<(ushort x, ushort y)> GatherValidTiles(WorldGenHandler handler, BiomeFeature biome)
    {
        var result = new List<(ushort, ushort)>();

        if (biome != null)
        {
            var noiseX = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyX);
            var noiseY = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyY);

            foreach ((ushort cx, ushort cy) in biome.BiomeChunks)
            {
                foreach ((ushort tx, ushort ty) in handler.GetChunk(cx, cy).IterateWorldTiles())
                {
                    if (!biome.IsInBiome(noiseX.Sample(tx, ty), noiseY.Sample(tx, ty))) continue;
                    if (IsAllowedTile(handler.GetTileType(tx, ty)))
                        result.Add((tx, ty));
                }
            }
        }
        else
        {
            int total = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;
            for (ushort tx = 0; tx < total; tx++)
                for (ushort ty = 0; ty < total; ty++)
                    if (IsAllowedTile(handler.GetTileType(tx, ty)))
                        result.Add((tx, ty));
        }

        return result;
    }

    private bool IsAllowedTile(TileType type)
    {
        foreach (TileType allowed in AllowedTileTypes)
            if (type == allowed) return true;
        return false;
    }
}
