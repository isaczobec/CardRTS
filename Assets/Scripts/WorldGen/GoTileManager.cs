using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GoTileObjectSettings
{
    public GameObject Prefab;
    public float ChanceToSpawn; // 0 to 1, relative to other objects in the same tile. Higher = more likely to spawn.
    public bool randomRotation;
    public float minScale = 1f;
    public float maxScale = 1f;
}

[System.Serializable]
public class GoTile
{
    public TileType Type;
    public GoTileObjectSettings[] Objects;
}


public class GoTileManager : MonoBehaviour
{
    [SerializeField]
    private GoTile[] _goTiles;
    private Dictionary<TileType, GoTile> _tileLookup;

    // One parent per chunk coordinate, created lazily the first time a tile in that chunk
    // actually spawns something — lets WorldChunkVisibilityManager toggle every decoration
    // in a chunk (there can be hundreds) with a single SetActive call instead of one per
    // object. See WorldRenderer.ChunkObjects for the terrain-side equivalent.
    private readonly Dictionary<(int cx, int cy), Transform> _chunkRoots = new();
    public IReadOnlyDictionary<(int cx, int cy), Transform> ChunkRoots => _chunkRoots;

    private void Initialize()
    {
        _tileLookup = new Dictionary<TileType, GoTile>();
        foreach (var tile in _goTiles)
        {
            if (!_tileLookup.ContainsKey(tile.Type))
                _tileLookup.Add(tile.Type, tile);
            else
                Debug.LogWarning($"Duplicate GoTile entry for TileType {tile.Type} in GoTileManager.");
        }
    }

    private void Awake()
    {
        Initialize();
    }

    public bool isGoTile(TileType type)
    {
        return _tileLookup.ContainsKey(type);
    }

    public GameObject TrySpawnGoTile(ushort tileX, ushort tileY)
    {
        var tileType = WorldManager.instance.Handler.GetTileType(tileX, tileY);
        if (!_tileLookup.TryGetValue(tileType, out var goTile))
            return null;

        var settings = goTile.Objects;
        if (settings == null || settings.Length == 0)
            return null;

        // Choose a prefab to spawn based on the ChanceToSpawn weights.
        float totalChance = 0f;
        foreach (var s in settings)
            totalChance += s.ChanceToSpawn;

        float randomValue = Random.Range(0f, totalChance);
        GoTileObjectSettings chosenSettings = null;
        foreach (var s in settings)
        {
            if (randomValue < s.ChanceToSpawn)
            {
                chosenSettings = s;
                break;
            }
            randomValue -= s.ChanceToSpawn;
        }

        if (chosenSettings == null || chosenSettings.Prefab == null)
            return null;

        // Spawn the prefab with optional random rotation and scale, parented under this
        // tile's chunk root (see _chunkRoots) rather than left loose in the scene.
        var instance = Instantiate(chosenSettings.Prefab, GetOrCreateChunkRoot(tileX, tileY));
        instance.transform.position = WorldManager.instance.TileToWorldPosition(tileX, tileY, center: true);

        if (chosenSettings.randomRotation)
            instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        float scale = Random.Range(chosenSettings.minScale, chosenSettings.maxScale);
        instance.transform.localScale = new Vector3(scale, scale, scale);

        return instance;
    }

    private Transform GetOrCreateChunkRoot(ushort tileX, ushort tileY)
    {
        int chunkX = tileX / WorldGenHandler.CHUNK_SIZE_TILES;
        int chunkY = tileY / WorldGenHandler.CHUNK_SIZE_TILES;
        var key = (chunkX, chunkY);

        if (_chunkRoots.TryGetValue(key, out Transform root))
            return root;

        var go = new GameObject($"GoTileChunk_{chunkX}_{chunkY}");
        go.transform.SetParent(transform, false);
        _chunkRoots[key] = go.transform;
        return go.transform;
    }
}