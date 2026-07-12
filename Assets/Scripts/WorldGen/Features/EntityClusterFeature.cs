using System;
using System.Collections.Generic;
using UnityEngine;

// Places entity clusters within the parent biome (or across the whole world if there is
// no parent BiomeFeature). Set Spawner to any EntitySpawnAction.SpawnXxx delegate to
// control what gets placed; ClusterCount * EntitiesPerCluster actions are enqueued, each
// carrying the computed position and the shared Spawner.
//
// Usage — as a biome child:
//   new EntityClusterFeature
//   {
//       Spawner            = EntitySpawnAction.SpawnTree,
//       ClusterCount       = 4,
//       EntitiesPerCluster = 6,
//   }
public class EntityClusterFeature : WorldGenFeature
{
    public Action<ulong, ECS> Spawner;
    public int ClusterCount = 5;
    public int EntitiesPerCluster = 8;
    public float ClusterRadius = 5f;
    public int Seed = 1234;
    public TileType[] AllowedTileTypes = { TileType.Grass };

    public override void Generate(WorldGenHandler handler)
    {
        var rng = new System.Random(Seed);
        var biome = handler.GetPreviousFeature<BiomeFeature>();

        List<(ushort x, ushort y)> validTiles = GatherValidTiles(handler, biome);
        if (validTiles.Count == 0) return;

        float maxCoord = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1f;

        for (int c = 0; c < ClusterCount; c++)
        {
            (ushort cx, ushort cy) = validTiles[rng.Next(validTiles.Count)];

            for (int e = 0; e < EntitiesPerCluster; e++)
            {
                float angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
                float dist  = (float)(rng.NextDouble() * ClusterRadius);

                float x = Mathf.Clamp(cx + Mathf.Cos(angle) * dist, 0f, maxCoord);
                float y = Mathf.Clamp(cy + Mathf.Sin(angle) * dist, 0f, maxCoord);

                handler.EnqueueAction(new EntitySpawnAction { X = x, Y = y, Spawner = Spawner });
            }
        }
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
