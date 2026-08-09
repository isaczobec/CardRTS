using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct BridgeRegistryEntry
{
    // Identifies this bridge type to WorldGenFeatures (e.g. IslandBridgeFeature.BridgeTypeName,
    // WedgeIslandFeature.BridgeTypeName) — matched by exact (case-sensitive) string.
    public string Name;

    // Candidate segment prefabs for this bridge type — each must have a
    // BridgeSegmentFootprint component (see that class). One is picked at random
    // (deterministically, via handler.Random) per connection built with this type.
    public GameObject[] Prefabs;
}

/// <summary>
/// Maps bridge type names to a pool of segment prefabs via Inspector-assigned (Name, Prefabs)
/// pairs (see WorldManager.BridgeRegistry, where this is assigned), so a WorldGenFeature can
/// ask for a bridge type by name — e.g. "MainNetwork", "WedgeConnector" — instead of holding
/// prefab references itself. Mirrors IslandRegistry's own role for islands: different features
/// (or the same feature, for different kinds of connection) can be pointed at different named
/// entries so, for example, the main ring/spoke network reads visually distinct from the
/// wedge-filler connections between them.
/// </summary>
public class BridgeRegistry : MonoBehaviour
{
    public BridgeRegistryEntry[] Entries;

    private Dictionary<string, GameObject[]> _byName;

    public bool TryGetPrefabs(string name, out GameObject[] prefabs)
    {
        if (_byName == null) BuildLookup();
        return _byName.TryGetValue(name, out prefabs);
    }

    private void BuildLookup()
    {
        _byName = new Dictionary<string, GameObject[]>();
        if (Entries == null) return;

        foreach (BridgeRegistryEntry entry in Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                Debug.LogWarning("[BridgeRegistry] An entry has no Name — skipped.");
                continue;
            }
            if (_byName.ContainsKey(entry.Name))
            {
                Debug.LogWarning($"[BridgeRegistry] Duplicate bridge type name '{entry.Name}' — keeping the first one registered.");
                continue;
            }
            _byName[entry.Name] = entry.Prefabs ?? System.Array.Empty<GameObject>();
        }
    }
}
