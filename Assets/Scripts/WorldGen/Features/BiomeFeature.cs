using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a biome as a rectangular region in the 2D space spanned by two noise generators.
/// During Generate, marks which chunks contain at least one tile belonging to the biome,
/// then enqueues any child features so they run immediately after.
///
/// Child features can retrieve this instance via handler.GetPreviousFeature&lt;BiomeFeature&gt;()
/// and use BiomeChunks, IsInBiome, and DistanceToBiomeCutoff.
/// </summary>
public class BiomeFeature : WorldGenFeature
{
    /// <summary>Keys of the two noise generators that form the biome coordinate axes.</summary>
    public string NoiseKeyX;
    public string NoiseKeyY;

    /// <summary>Inclusive bounds of this biome in [0,1]^2 noise space.</summary>
    public float MinX, MaxX;
    public float MinY, MaxY;

    /// <summary>Features enqueued as children of this biome in insertion order.</summary>
    public WorldGenFeature[] ChildFeatures = System.Array.Empty<WorldGenFeature>();

    /// <summary>
    /// Populated by Generate. Contains every chunk that has at least one tile inside the biome.
    /// </summary>
    public IReadOnlyList<(ushort chunkX, ushort chunkY)> BiomeChunks { get; private set; }
        = System.Array.Empty<(ushort, ushort)>();

    /// <summary>Returns true when the given noise-space point falls within the biome box.</summary>
    public bool IsInBiome(float noiseX, float noiseY)
        => noiseX >= MinX && noiseX <= MaxX && noiseY >= MinY && noiseY <= MaxY;

    /// <summary>
    /// Returns the normalized distance from the given noise-space point to the nearest biome
    /// boundary, in [0, 1]. 0 means on or outside the boundary; 1 means at the inscribed
    /// centre (maximally inside). Useful for smooth blending near biome edges.
    /// </summary>
    public float DistanceToBiomeCutoff(float noiseX, float noiseY)
    {
        float dx = Mathf.Min(noiseX - MinX, MaxX - noiseX);
        float dy = Mathf.Min(noiseY - MinY, MaxY - noiseY);
        float rawDist = Mathf.Min(dx, dy);

        float halfW = (MaxX - MinX) * 0.5f;
        float halfH = (MaxY - MinY) * 0.5f;
        float maxDist = Mathf.Min(halfW, halfH);

        if (maxDist <= 0f) return 0f;
        return Mathf.Clamp01(rawDist / maxDist);
    }

    public override void Generate(WorldGenHandler handler)
    {
        var noiseX = handler.GetWorldGenResource<NoiseGenerator>(NoiseKeyX);
        var noiseY = handler.GetWorldGenResource<NoiseGenerator>(NoiseKeyY);

        int tileSize  = WorldGenHandler.CHUNK_SIZE_TILES;
        int numChunks = WorldGenHandler.WorldSizeChunks;

        var chunks = new List<(ushort, ushort)>();

        for (ushort cy = 0; cy < numChunks; cy++)
        {
            for (ushort cx = 0; cx < numChunks; cx++)
            {
                bool found = false;
                for (ushort iy = 0; iy < tileSize && !found; iy++)
                {
                    for (ushort ix = 0; ix < tileSize && !found; ix++)
                    {
                        ushort tx = (ushort)(cx * tileSize + ix);
                        ushort ty = (ushort)(cy * tileSize + iy);
                        if (IsInBiome(noiseX.Sample(tx, ty), noiseY.Sample(tx, ty)))
                            found = true;
                    }
                }
                if (found)
                    chunks.Add((cx, cy));
            }
        }

        BiomeChunks = chunks;

        foreach (var feature in ChildFeatures)
            handler.EnqueueFeature(feature);
    }
}
