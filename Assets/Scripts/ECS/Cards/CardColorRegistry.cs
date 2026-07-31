using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

[System.Serializable]
public class CardColorRegistryEntry
{
    public string id;
    public UnityEngine.Color backgroundColor;
    public UnityEngine.Color textBackgroundColor;
    public UnityEngine.Color edgeColor;
}

/// <summary>
/// String -> Color lookup for card-face background colors (Card.ColorRegistryId, which
/// defaults to Card.ImageName). Populate _entries in the Inspector; one entry per card that
/// wants its own themed BackgroundColor — a card with no entry falls back to Card.
/// BackgroundColor's own default. Mirrors ImageRegistry exactly, just Color instead of Sprite.
/// </summary>
public class CardColorRegistry : Singleton<CardColorRegistry>
{
    [SerializeField] private List<CardColorRegistryEntry> _entries = new List<CardColorRegistryEntry>();

    private Dictionary<string, CardColorRegistryEntry> _lookup;

    protected override void Awake()
    {
        base.Awake();

        _lookup = new Dictionary<string, CardColorRegistryEntry>();
        foreach (CardColorRegistryEntry entry in _entries)
        {
            if (string.IsNullOrEmpty(entry.id)) continue;
            _lookup[entry.id] = entry;
        }
    }

    public bool TryGetBG(string id, out UnityEngine.Color color)
    {
        if (_lookup == null || string.IsNullOrEmpty(id))
        {
            color = default;
            return false;
        }
        bool success = _lookup.TryGetValue(id, out CardColorRegistryEntry c);
        color = success ? c.backgroundColor : new UnityEngine.Color(0f, 0f, 0f);
        return success;
    }
    public bool TryGetTextBG(string id, out UnityEngine.Color color)
    {
        if (_lookup == null || string.IsNullOrEmpty(id))
        {
            color = default;
            return false;
        }
        bool success = _lookup.TryGetValue(id, out CardColorRegistryEntry c);
        color = success ? c.textBackgroundColor : new UnityEngine.Color(0f, 0f, 0f);
        return success;
    }
    public bool TryGetEdge(string id, out UnityEngine.Color color)
    {
        if (_lookup == null || string.IsNullOrEmpty(id))
        {
            color = default;
            return false;
        }
        bool success = _lookup.TryGetValue(id, out CardColorRegistryEntry c);
        color = success ? c.edgeColor : new UnityEngine.Color(0f, 0f, 0f);
        return success;
    }
}
