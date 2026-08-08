// Sets every tile in the world to FillType. Typically the first feature in the pipeline,
// establishing a uniform base — e.g. TileType.Air as the "void" a floating-island world
// starts from — for later features to stamp over.
public class FillWorldFeature : WorldGenFeature
{
    public TileType FillType;

    public override void Generate(WorldGenHandler handler)
    {
        ushort worldSize = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);
        for (ushort ty = 0; ty < worldSize; ty++)
            for (ushort tx = 0; tx < worldSize; tx++)
                handler.SetTileType(tx, ty, FillType);
    }
}
