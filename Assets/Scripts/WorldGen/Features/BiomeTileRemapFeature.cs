/// <summary>
/// Must be a child of a BiomeFeature. For every tile that belongs to the biome,
/// replaces any occurrence of <see cref="From"/> with <see cref="To"/>.
/// Uses the parent BiomeFeature's noise keys and chunk list so no redundant sampling
/// or iteration happens outside the biome footprint.
/// </summary>
public class BiomeTileRemapFeature : WorldGenFeature
{
    public TileType From;
    public TileType To;

    public override void Generate(WorldGenHandler handler)
    {
        var biome = handler.GetPreviousFeature<BiomeFeature>();
        if (biome == null)
        {
            UnityEngine.Debug.LogWarning("[BiomeTileRemapFeature] No parent BiomeFeature found.");
            return;
        }

        var noiseX = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyX);
        var noiseY = handler.GetWorldGenResource<NoiseGenerator>(biome.NoiseKeyY);

        foreach (var (cx, cy) in biome.BiomeChunks)
        {
            var chunk = handler.GetChunk(cx, cy);
            foreach (var (tx, ty) in chunk.IterateWorldTiles())
            {
                if (!biome.IsInBiome(noiseX.Sample(tx, ty), noiseY.Sample(tx, ty)))
                    continue;
                if (handler.GetTileType(tx, ty) == From)
                    handler.SetTileType(tx, ty, To);
            }
        }
    }
}
