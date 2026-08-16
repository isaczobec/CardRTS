
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
    public static ushort WorldSizeChunks = 32;
    private TileType[] _tiles;
    private float[] _heightMap;

    public List<WorldGenResource> resources = new List<WorldGenResource>();
    public List<WorldGenFeature> features = new List<WorldGenFeature>();
    private readonly List<IWorldGenAction> _pendingActions = new();

    // Populated externally (see WorldManager.GenerateAndRender) before Generate() runs, so
    // a feature can spawn/position things per connected player (e.g. SpawnPlayerBasesFeature).
    public List<ushort> ConnectedClientIds = new();

    // Set externally before Generate() runs (see WorldManager.GenerateAndRender) — must be
    // identical on every machine generating this world, like ConnectedClientIds, since it
    // seeds Random below. Every feature that needs deterministic randomness should draw
    // from Random rather than seeding its own RNG, so a given Seed always reproduces the
    // exact same world regardless of how many features consume random values or in what
    // order (as long as that order itself is deterministic, which it is — SetupWorldGen
    // always builds the same feature list).
    public int Seed;
    public Random Random { get; private set; }

    // Accumulates the coarse island/bridge region graph as this handler's own features place
    // islands and commit bridges (see IslandPlacementHelper.TryPlaceIsland/
    // BridgeConnectionBuilder.Commit, the only two places that ever call into it). Read once
    // generation finishes by NavMeshHandler.CreateNavMesh (see WorldManager.GenerateAndRender)
    // to build Pathfinding's hierarchical search corridor. Always non-null — simply stays
    // empty for a world whose features never place an island (see IslandGraphBuilder.GetRegionId).
    public IslandGraphBuilder IslandGraph { get; } = new IslandGraphBuilder((ushort)(CHUNK_SIZE_TILES * WorldSizeChunks));

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

    // Plural counterpart to GetPreviousFeature — returns every earlier feature of type T
    // (in generation order) rather than just the nearest one. Used where a feature needs to
    // see everything a whole family of earlier features produced, e.g.
    // PatchScatterFeature.MinDistanceToOtherPatches reading every earlier
    // PatchScatterFeature's Patches list, not just the last one.
    public List<T> GetPreviousFeatures<T>(Func<WorldGenFeature, bool> predicate = null) where T : WorldGenFeature
    {
        var result = new List<T>();
        for (int i = _currentFeatureIndex - 1; i >= 0; i--)
        {
            if (features[i] is T typed && (predicate == null || predicate(features[i])))
                result.Add(typed);
        }
        return result;
    }

    public void EnqueueFeature(WorldGenFeature feature)
    {
        features.Insert(_currentFeatureLastChildIndex+1, feature);
        _currentFeatureLastChildIndex++;
    }

    public void EnqueueAction(IWorldGenAction action)
    {
        _pendingActions.Add(action);

        // Keeps the SpawnedEntityRegistry (if one was added — see WorldManager.SetupWorldGen)
        // in sync automatically, so features don't need to remember to register spawns themselves.
        if (action is EntitySpawnAction spawnAction)
            GetWorldGenResource<SpawnedEntityRegistry>(SpawnedEntityRegistry.ResourceKey)?.Register(spawnAction);
    }

    // Returns every currently-pending action of type T for which predicate (if given)
    // returns true. Mirrors GetPreviousFeature's shape, but actions — unlike features —
    // have no execution order to search "backward" through (they all run together at the
    // end, via ExecuteActions), so this just scans every pending action.
    public List<T> GetActions<T>(Func<T, bool> predicate = null) where T : class, IWorldGenAction
    {
        List<T> result = new List<T>();
        foreach (IWorldGenAction action in _pendingActions)
            if (action is T typed && (predicate == null || predicate(typed)))
                result.Add(typed);
        return result;
    }

    // Removes a single previously-enqueued action (e.g. one returned by GetActions) so it
    // never runs. Returns false if it wasn't pending.
    public bool RemoveAction(IWorldGenAction action)
    {
        bool removed = _pendingActions.Remove(action);
        if (removed && action is EntitySpawnAction spawnAction)
            GetWorldGenResource<SpawnedEntityRegistry>(SpawnedEntityRegistry.ResourceKey)?.Unregister(spawnAction);
        return removed;
    }

    public void ExecuteActions(ECS ecs)
    {
        foreach (IWorldGenAction action in _pendingActions)
            action.Execute(ecs);
        _pendingActions.Clear();
    }

    public void Generate()
    {
        Random = new Random(Seed);
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

    public float GetHeight(ushort tileX, ushort tileY)
    {
        if (tileX >= CHUNK_SIZE_TILES * WorldSizeChunks || tileY >= CHUNK_SIZE_TILES * WorldSizeChunks)
        {
            DebugLogger.LogWarning($"Tile coordinates ({tileX}, {tileY}) are out of bounds.");
            return 0f;
        }
        return _heightMap[TileXYToIndex(tileX, tileY)];
    }

    public float GetHeight(ushort chunkX, ushort chunkY, ushort inChunkX, ushort inChunkY)
    {
        ushort x = (ushort)(chunkX * CHUNK_SIZE_TILES + inChunkX);
        ushort y = (ushort)(chunkY * CHUNK_SIZE_TILES + inChunkY);
        return GetHeight(x, y);
    }

    public bool SetHeight(ushort tileX, ushort tileY, float height)
    {
        if (tileX >= CHUNK_SIZE_TILES * WorldSizeChunks || tileY >= CHUNK_SIZE_TILES * WorldSizeChunks)
            return false;
        _heightMap[TileXYToIndex(tileX, tileY)] = height;
        return true;
    }

    public bool SetHeight(ushort chunkX, ushort chunkY, ushort inChunkX, ushort inChunkY, float height)
    {
        ushort x = (ushort)(chunkX * CHUNK_SIZE_TILES + inChunkX);
        ushort y = (ushort)(chunkY * CHUNK_SIZE_TILES + inChunkY);
        return SetHeight(x, y, height);
    }

    // Returns int — max index for a 1024×1024 world is ~1M, which overflows ushort.
    public int TileXYToIndex(ushort tileX, ushort tileY)
    {
        int W = CHUNK_SIZE_TILES;
        int C = WorldSizeChunks;
        return (tileY / W * C + tileX / W) * W * W + tileY % W * W + tileX % W;
    }

    public (ushort tileX, ushort tileY) TileIndexToXY(int index)
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
    Water = 0,
    Sand = 1,
    Grass = 2,
    Mountain = 3,
    Snow = 4,
    Gravel = 5,
    Ice = 6,
    // Void — the default fill for a floating-island world (see FillWorldFeature). Must be
    // configured with HasCollision = true in WorldManager's Tile Settings so nothing can
    // walk/spawn/path over open space between islands.
    Air = 7,
    // Walkable ground stamped under an island prefab's footprint (see IslandFootprint /
    // IslandPlayerBaseFeature). Must be configured with HasCollision = false.
    Island = 8,
    // Walkable ground stamped along a bridge's curve between two islands (see
    // IslandBridgeFeature / BridgeSegmentFootprint). Must be configured with
    // HasCollision = false.
    Bridge = 9,
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

    public float GetHeight(ushort inChunkX, ushort inChunkY)
    {
        return _parent.GetHeight(_chunkX, _chunkY, inChunkX, inChunkY);
    }

    public bool SetHeight(ushort inChunkX, ushort inChunkY, float height)
    {
        return _parent.SetHeight(_chunkX, _chunkY, inChunkX, inChunkY, height);
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