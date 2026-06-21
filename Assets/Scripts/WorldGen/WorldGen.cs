
using System;
using System.Collections;
using System.Collections.Generic;

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
    private float[] _heightMap;

    public List<WorldGenResource> resources = new List<WorldGenResource>();
    public List<WorldGenFeature> features = new List<WorldGenFeature>();
    private int _currentFeatureIndex = 0;
    private WorldGenFeature _currentFeature => features[_currentFeatureIndex];
    private int _currentFeatureLastChildIndex = 0;
    public void AddResource(WorldGenResource resource)
    {
        resources.Add(resource);
    }
    public T GetWorldGenResource<T>(string identifier) where T : class, WorldGenResource
    {
        foreach (var resource in resources)
        {
            if (resource.Identifier == identifier)
            {
                if (!(resource is T))
                    throw new System.Exception($"Resource with identifier {identifier} is not of type {typeof(T).Name}");
                return (T)resource;
            }
        }
        return null;
    }

    public T GetPreviousFeature<T>(Func<WorldGenFeature, bool> predicate = null) where T : WorldGenFeature
    {
        for (int i = _currentFeatureIndex - 1; i >= 0; i--)
        {
            if (features[i] is T && (predicate == null || predicate(features[i])))
                return (T)features[i];
        }
        return null;
    }

    public void EnqueueFeature(WorldGenFeature feature)
    {
        features.Insert(_currentFeatureLastChildIndex+1, feature);
        _currentFeatureLastChildIndex++;
    }

    public void Generate()
    {
        _tiles = new TileType[CHUNK_SIZE_TILES * CHUNK_SIZE_TILES * WorldSizeChunks * WorldSizeChunks];
        _heightMap = new float[CHUNK_SIZE_TILES * CHUNK_SIZE_TILES * WorldSizeChunks * WorldSizeChunks];
        for (_currentFeatureIndex = 0; _currentFeatureIndex < features.Count; _currentFeatureIndex++)
        {
            _currentFeatureLastChildIndex = _currentFeatureIndex;
            features[_currentFeatureIndex].Generate(this);
        }
    }

    public TileType GetTileType(ushort chunkX, ushort chunkY, ushort inChunkX, ushort inChunkY)
    {
        ushort x = (ushort)(chunkX * CHUNK_SIZE_TILES + inChunkX);
        ushort y = (ushort)(chunkY * CHUNK_SIZE_TILES + inChunkY);
        return _tiles[TileXYToIndex(x, y)];
    }

    public TileType GetTileType(ushort tileX, ushort tileY)
    {
        return _tiles[TileXYToIndex(tileX, tileY)];
    }

    public bool SetTileType(ushort tileX, ushort tileY, TileType type)
    {
        if (tileX >= CHUNK_SIZE_TILES * WorldSizeChunks || tileY >= CHUNK_SIZE_TILES * WorldSizeChunks)
            return false;
        _tiles[TileXYToIndex(tileX, tileY)] = type;
        return true;
    }

    public bool SetTileType(ushort chunkX, ushort chunkY, ushort inChunkX, ushort inChunkY, TileType type)
    {
        ushort x = (ushort)(chunkX * CHUNK_SIZE_TILES + inChunkX);
        ushort y = (ushort)(chunkY * CHUNK_SIZE_TILES + inChunkY);
        return SetTileType(x, y, type);
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

    public Chunk GetChunk(ushort chunkX, ushort chunkY)
    {
        return new Chunk(this, chunkX, chunkY);
    }

    public Chunk GetChunkFromTile(ushort tileX, ushort tileY)
    {
        ushort chunkX = (ushort)(tileX / CHUNK_SIZE_TILES);
        ushort chunkY = (ushort)(tileY / CHUNK_SIZE_TILES);
        return GetChunk(chunkX, chunkY);
    }

    public IEnumerable<Chunk> IterateChunks()
    {
        for (ushort chunkY = 0; chunkY < WorldSizeChunks; chunkY++)
        {
            for (ushort chunkX = 0; chunkX < WorldSizeChunks; chunkX++)
            {
                yield return GetChunk(chunkX, chunkY);
            }
        }
    }
}

public enum TileType : byte
{
    
}

/// <summary>
/// Wrapper for a chunk of tiles. Each chunk is CHUNK_SIZE_TILES by CHUNK_SIZE_TILES in size, and the world is WorldSizeChunks by WorldSizeChunks chunks in size.
/// </summary>
public class Chunk
{
    private WorldGenHandler _parent;
    private ushort _chunkX;
    private ushort _chunkY;
    public Chunk(WorldGenHandler parent, ushort chunkX, ushort chunkY)
    {
        _parent = parent;
        _chunkX = chunkX;
        _chunkY = chunkY;
    }

    public TileType GetTileType(ushort inChunkX, ushort inChunkY)
    {
        return _parent.GetTileType(_chunkX, _chunkY, inChunkX, inChunkY);
    }

    public bool SetTileType(ushort inChunkX, ushort inChunkY, TileType type)
    {
        return _parent.SetTileType(_chunkX, _chunkY, inChunkX, inChunkY, type);
    }
    
    // function to iterate all (x, y) pairs in the chunk in world space (returns world tile coordinates)
    public IEnumerable<(ushort tileX, ushort tileY)> IterateWorldTiles()
    {
        for (ushort inChunkY = 0; inChunkY < WorldGenHandler.CHUNK_SIZE_TILES; inChunkY++)
        {
            for (ushort inChunkX = 0; inChunkX < WorldGenHandler.CHUNK_SIZE_TILES; inChunkX++)
            {
                ushort tileX = (ushort)(inChunkX + _chunkX * WorldGenHandler.CHUNK_SIZE_TILES);
                ushort tileY = (ushort)(inChunkY + _chunkY * WorldGenHandler.CHUNK_SIZE_TILES);
                yield return (tileX, tileY);
            }
        }
    }

    public IEnumerable<(ushort inChunkX, ushort inChunkY)> IterateInChunkTiles()
    {
        for (ushort inChunkY = 0; inChunkY < WorldGenHandler.CHUNK_SIZE_TILES; inChunkY++)
        {
            for (ushort inChunkX = 0; inChunkX < WorldGenHandler.CHUNK_SIZE_TILES; inChunkX++)
            {
                yield return (inChunkX, inChunkY);
            }
        }
    }
}

/// <summary>
/// For common noise generators etc.
/// </summary>
public interface WorldGenResource
{
    public string Identifier { get; }
}

public abstract class WorldGenFeature
{
    public abstract void Generate(WorldGenHandler handler);
}