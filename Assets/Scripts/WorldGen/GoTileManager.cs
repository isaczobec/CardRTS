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

        // Spawn the prefab with optional random rotation and scale.
        var instance = Instantiate(chosenSettings.Prefab);
        instance.transform.position = WorldManager.instance.TileToWorldPosition(tileX, tileY);

        if (chosenSettings.randomRotation)
            instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        float scale = Random.Range(chosenSettings.minScale, chosenSettings.maxScale);
        instance.transform.localScale = new Vector3(scale, scale, scale);

        return instance;
    }

}