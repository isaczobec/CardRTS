using System.Collections.Generic;

/// <summary>
/// Paints tile types across the entire map by thresholding a noise generator.
/// Thresholds must be sorted ascending by MaxValue; the last entry acts as the default.
/// </summary>
public class NoiseTileFeature : WorldGenFeature
{
    public string NoiseResourceKey;
    public List<NoiseThreshold> Thresholds = new();

    public override void Generate(WorldGenHandler handler)
    {
        var noise = handler.GetWorldGenResource<NoiseGenerator>(NoiseResourceKey);
        ushort size = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);

        for (ushort x = 0; x < size; x++)
        {
            for (ushort y = 0; y < size; y++)
            {
                float value = noise.Sample(x, y);
                TileType type = Thresholds[^1].Type;
                foreach (var entry in Thresholds)
                {
                    if (value <= entry.MaxValue)
                    {
                        type = entry.Type;
                        break;
                    }
                }
                handler.SetTileType(x, y, type);
            }
        }
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
