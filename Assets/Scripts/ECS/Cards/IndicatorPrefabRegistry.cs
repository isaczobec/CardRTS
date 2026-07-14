using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class IndicatorPrefabRegistryEntry
{
    public string name;
    public GameObject prefab;
}

/// <summary>
/// String -> prefab lookup for SpawnAtPointCard.IndicatorPrefabName. Populate _entries in
/// the Inspector; one entry per indicator prefab name a SpawnAtPointCard class returns.
/// Mirrors ImageRegistry's shape.
/// </summary>
public class IndicatorPrefabRegistry : Singleton<IndicatorPrefabRegistry>
{
    [SerializeField] private List<IndicatorPrefabRegistryEntry> _entries = new List<IndicatorPrefabRegistryEntry>();

    private Dictionary<string, GameObject> _lookup;

    protected override void Awake()
    {
        base.Awake();

        _lookup = new Dictionary<string, GameObject>();
        foreach (IndicatorPrefabRegistryEntry entry in _entries)
        {
            if (string.IsNullOrEmpty(entry.name) || entry.prefab == null) continue;
            _lookup[entry.name] = entry.prefab;
        }
    }

    public bool TryGet(string name, out GameObject prefab)
    {
        if (_lookup == null || string.IsNullOrEmpty(name))
        {
            prefab = null;
            return false;
        }
        return _lookup.TryGetValue(name, out prefab);
    }
}
