using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct IslandRegistryEntry
{
    // Identifies this island type to WorldGenFeatures (e.g.
    // IslandPlayerBaseFeature.IslandNames) — matched by exact (case-sensitive) string.
    public string Name;

    // Must have an IslandFootprint component (see that class) — validated by whoever
    // actually places it (e.g. IslandPlayerBaseFeature), not here.
    public GameObject Prefab;
}

/// <summary>
/// Maps island type names to prefabs via Inspector-assigned (Name, Prefab) pairs (see
/// WorldManager.IslandRegistry, where this is assigned), so a WorldGenFeature can ask for an
/// island by name — e.g. "PlayerBase", "Outpost" — instead of holding direct prefab
/// references itself. New island types/variants can then be added as pure Inspector data
/// without touching the features that place them.
/// </summary>
public class IslandRegistry : MonoBehaviour
{
    public IslandRegistryEntry[] Entries;

    private Dictionary<string, GameObject> _byName;

    public bool TryGetPrefab(string name, out GameObject prefab)
    {
        if (_byName == null) BuildLookup();
        return _byName.TryGetValue(name, out prefab);
    }

    // Resolves every name in names to its registered prefab — a name not found in the
    // registry is skipped with a warning rather than failing the whole batch, so one bad
    // name doesn't prevent every other valid one from still resolving.
    public List<GameObject> GetPrefabs(IEnumerable<string> names)
    {
        var result = new List<GameObject>();
        if (names == null) return result;

        foreach (string name in names)
        {
            if (TryGetPrefab(name, out GameObject prefab))
                result.Add(prefab);
            else
                Debug.LogWarning($"[IslandRegistry] No island registered under name '{name}'.");
        }
        return result;
    }

    private void BuildLookup()
    {
        _byName = new Dictionary<string, GameObject>();
        if (Entries == null) return;

        foreach (IslandRegistryEntry entry in Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                Debug.LogWarning($"[IslandRegistry] Entry for prefab '{(entry.Prefab != null ? entry.Prefab.name : "null")}' has no Name — skipped.");
                continue;
            }
            if (_byName.ContainsKey(entry.Name))
            {
                Debug.LogWarning($"[IslandRegistry] Duplicate island name '{entry.Name}' — keeping the first one registered.");
                continue;
            }
            _byName[entry.Name] = entry.Prefab;
        }
    }
}
