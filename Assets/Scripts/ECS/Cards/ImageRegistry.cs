using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ImageRegistryEntry
{
    public string name;
    public Sprite sprite;
}

/// <summary>
/// String -> Sprite lookup for card artwork (Card.ImageName). Populate _entries in the
/// Inspector; one entry per image name a Card class returns.
/// </summary>
public class ImageRegistry : Singleton<ImageRegistry>
{
    [SerializeField] private List<ImageRegistryEntry> _entries = new List<ImageRegistryEntry>();

    private Dictionary<string, Sprite> _lookup;

    protected override void Awake()
    {
        base.Awake();

        _lookup = new Dictionary<string, Sprite>();
        foreach (ImageRegistryEntry entry in _entries)
        {
            if (string.IsNullOrEmpty(entry.name) || entry.sprite == null) continue;
            _lookup[entry.name] = entry.sprite;
        }
    }

    public bool TryGet(string name, out Sprite sprite)
    {
        if (_lookup == null || string.IsNullOrEmpty(name))
        {
            sprite = null;
            return false;
        }
        return _lookup.TryGetValue(name, out sprite);
    }
}
