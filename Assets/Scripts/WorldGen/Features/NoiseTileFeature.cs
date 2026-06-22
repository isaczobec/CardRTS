using System.Collections.Generic;

/// <summary>
/// Paints tile types by thresholding a noise generator.
/// When used as a child of a BiomeFeature it operates only on in-biome tiles;
/// <see cref="BiomeBorderDistanceThreshold"/> further restricts it to tiles whose
/// normalised distance from the biome boundary is at or above the threshold (0 = all
/// in-biome tiles, 0.2 = skip the outer 20% border strip).
/// Tiles whose current type appears in <see cref="PreserveTileTypes"/> are never
/// overwritten. Without a parent biome it paints the entire world.
/// Thresholds must be sorted ascending by MaxValue; the last entry acts as the default.
/// </summary>
public class NoiseTileFeature : WorldGenFeature
{
    public string NoiseResourceKey;
    public List<NoiseThreshold> Thresholds = new();

    /// <summary>
    /// Only used when this feature is a child of a BiomeFeature.
    /// Tiles with DistanceToBiomeCutoff below this value are skipped.
    /// Range [0, 1]; default 0 means the full biome interior is painted.
    /// </summary>
    public float BiomeBorderDistanceThreshold = 0f;

    /// <summary>
    /// Tile types that must not be overwritten by this feature.
    /// Checked for both global and biome-scoped passes.
    /// </summary>
    public TileType[] PreserveTileTypes = System.Array.Empty<TileType>();

    /// <summary>
    /// If the sampled noise value is below this threshold the tile is left untouched.
    /// Range [0, 1]; default 0 means all noise values produce a paint operation.
    /// </summary>
    public float NoiseMinThreshold = 0f;

    public override void Generate(WorldGenHandler handler)
    {
        var noise = handler.GetWorldGenResource<NoiseGenerator>(NoiseResourceKey);
        var preserve = new HashSet<TileType>(PreserveTileTypes);
        var biome = handler.GetPreviousFeature<BiomeFeature>();

        if (biome != null)
            GenerateBiome(handler, noise, biome, preserve);
        else
            GenerateGlobal(handler, noise, preserve);
    }

    void GenerateGlobal(WorldGenHandler handler, NoiseGenerator noise, HashSet<TileType> preserve)
    {
        ushort size = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        for (ushort x = 0; x < size; x++)
            for (ushort y = 0; y < size; y++)
            {
                if (preserve.Contains(handler.GetTileType(x, y)))
                    continue;
                var type = Sample(noise, x, y);
                if (type.HasValue)
                    handler.SetTileType(x, y, type.Value);
            }
    }

    void GenerateBiome(WorldGenHandler handler, NoiseGenerator noise, BiomeFeature biome, HashSet<TileType> preserve)
    {
        var biomeNoiseX = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyX);
        var biomeNoiseY = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyY);

        foreach (var (cx, cy) in biome.BiomeChunks)
        {
            var chunk = handler.GetChunk(cx, cy);
            foreach (var (tx, ty) in chunk.IterateWorldTiles())
            {
                float nx = biomeNoiseX.Sample(tx, ty);
                float ny = biomeNoiseY.Sample(tx, ty);
                if (!biome.IsInBiome(nx, ny))
                    continue;
                if (biome.DistanceToBiomeCutoff(nx, ny) < BiomeBorderDistanceThreshold)
                    continue;
                if (preserve.Contains(handler.GetTileType(tx, ty)))
                    continue;
                var type = Sample(noise, tx, ty);
                if (type.HasValue)
                    handler.SetTileType(tx, ty, type.Value);
            }
        }
    }

    // Returns null when the noise value is below NoiseMinThreshold, leaving the tile untouched.
    TileType? Sample(NoiseGenerator noise, ushort x, ushort y)
    {
        float value = noise.Sample(x, y);
        if (value < NoiseMinThreshold)
            return null;
        foreach (var entry in Thresholds)
            if (value <= entry.MaxValue)
                return entry.Type;
        return Thresholds[^1].Type;
    }
}

public struct NoiseThreshold
{
    public float MaxValue;
    public TileType Type;

    public NoiseThreshold(float maxValue, TileType type)
    {
        MaxValue = maxValue;
        Type = type;
    }
}
