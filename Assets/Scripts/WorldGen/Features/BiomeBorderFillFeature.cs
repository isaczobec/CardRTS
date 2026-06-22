/// <summary>
/// Must be a child of a BiomeFeature. Fills every in-biome tile whose normalised
/// distance to the biome boundary is at or below <see cref="Threshold"/> with
/// <see cref="FillType"/>. Distance 0 = boundary, 1 = inscribed centre, so a
/// Threshold of 0.2 produces a border strip covering the outer 20% of the biome.
/// </summary>
public class BiomeBorderFillFeature : WorldGenFeature
{
    /// <summary>Normalised distance from the boundary at which to fill [0, 1].</summary>
    public float Threshold = 0.2f;
    public TileType FillType;

    public override void Generate(WorldGenHandler handler)
    {
        var biome = handler.GetPreviousFeature<BiomeFeature>();
        if (biome == null)
        {
            UnityEngine.Debug.LogWarning("[BiomeBorderFillFeature] No parent BiomeFeature found.");
            return;
        }

        var noiseX = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyX);
        var noiseY = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyY);

        foreach (var (cx, cy) in biome.BiomeChunks)
        {
            var chunk = handler.GetChunk(cx, cy);
            foreach (var (tx, ty) in chunk.IterateWorldTiles())
            {
                float nx = noiseX.Sample(tx, ty);
                float ny = noiseY.Sample(tx, ty);
                if (!biome.IsInBiome(nx, ny))
                    continue;
                if (biome.DistanceToBiomeCutoff(nx, ny) <= Threshold)
                    handler.SetTileType(tx, ty, FillType);
            }
        }
    }
}
