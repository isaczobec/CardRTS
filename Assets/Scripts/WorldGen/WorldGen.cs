
public class WorldGenHandler
{
    /// <summary>
    /// The square root of the amount of tiles in a chunk.
    /// </summary>
    public const ushort CHUNK_SIZE_TILES = 16;
    /// <summary>
    /// The suqare root of the amount of chunks in the world. 
    /// </summary>
    public static ushort WorldSizeChunks = 64;
    private TileType[] _tiles;
    public TileType GetTileType(ushort chunkX, ushort chunkY, ushort inChunkX, ushort inChunkY)
    {
        
    }
    public ushort TileXYToIndex(ushort tileX, ushort tileY)
    {
        int W = CHUNK_SIZE_TILES;
        int C = WorldSizeChunks;
        return (ushort)((tileY / W * C + tileX / W) * W * W + tileY % W * W + tileX % W);
    }

    public (ushort tileX, ushort tileY) TileIndexToXY(ushort index)
    {
        int W = CHUNK_SIZE_TILES;
        int C = WorldSizeChunks;
        ushort tileX = (ushort)(index % W + index / (W * W) % C * W);
        ushort tileY = (ushort)(index / W % W + index / (W * W * C) * W);
        return (tileX, tileY);
    }
}

public enum TileType : byte
{
    
}

public class Chunk
{
    
}

public class WorldGenFeature
{
    
}