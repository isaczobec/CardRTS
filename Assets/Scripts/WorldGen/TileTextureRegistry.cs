using System;
using UnityEngine;

[Serializable]
public struct TileTextureEntry
{
    public TileType Type;
    public Texture2D Texture;
    public Color MapColor;
}

/// <summary>
/// Maps TileType enum values to Texture2Ds via explicit (Type, Texture) pairs assigned in
/// the Inspector. Order does not matter; BuildArray() places each texture at the slot
/// matching its enum value so the terrain shader can index by tile ID.
/// All textures must share the same dimensions; compression/format may differ freely
/// (BuildArray converts each one into the array's own format via Graphics.ConvertTexture).
/// </summary>
public class TileTextureRegistry : MonoBehaviour
{
    public TileTextureEntry[] Entries;

    // Indexed by (int)TileType for O(1) lookup, mirrors WorldManager's _settingsByType
    // pattern. Built lazily on first GetMapColor call rather than in Awake/Render, since
    // callers (e.g. MinimapManager) may look this up before Render() has run.
    private TileTextureEntry[] _entriesByType;

    public Color GetMapColor(TileType type)
    {
        if (_entriesByType == null) BuildEntryLookup();
        return _entriesByType[(int)type].MapColor;
    }

    private void BuildEntryLookup()
    {
        int count = Enum.GetValues(typeof(TileType)).Length;
        _entriesByType = new TileTextureEntry[count];
        if (Entries != null)
            foreach (var e in Entries)
                _entriesByType[(int)e.Type] = e;
    }

    public Texture2DArray BuildArray()
    {
        int count = Enum.GetValues(typeof(TileType)).Length;

        var textures = new Texture2D[count];
        if (Entries != null)
            foreach (var e in Entries)
                textures[(int)e.Type] = e.Texture;

        Texture2D first = null;
        foreach (var t in textures) { if (t != null) { first = t; break; } }

        if (first == null)
        {
            Debug.LogWarning("[TileTextureRegistry] No textures assigned.");
            return null;
        }

        bool useMips = first.mipmapCount > 1;
        // Fixed RGBA32 rather than "whatever pixel format the first assigned texture happens
        // to have" — tile textures aren't guaranteed to all share the same compression (e.g.
        // mixing an uncompressed placeholder with a compressed final texture, or just
        // importing new ones with different default settings), and Graphics.ConvertTexture
        // below can convert any source format into this one, unlike Graphics.CopyTexture
        // (used previously), which requires a byte-exact format match and throws a
        // "mismatching data size" error otherwise.
        var arr = new Texture2DArray(first.width, first.height, count, TextureFormat.RGBA32, useMips);

        for (int i = 0; i < count; i++)
        {
            var tex = textures[i];
            if (tex == null)
            {
                Debug.LogWarning($"[TileTextureRegistry] No texture assigned for TileType value {i}.");
                continue;
            }
            if (tex.width != first.width || tex.height != first.height)
            {
                Debug.LogError($"[TileTextureRegistry] Texture for TileType {(TileType)i} ('{tex.name}') is " +
                               $"{tex.width}x{tex.height} but the array expects {first.width}x{first.height}. " +
                               $"Set the same Max Texture Size on all tile textures in their import settings.");
                continue;
            }
            // GPU-side format conversion (handles mismatched compression/format between tex
            // and arr, and copies every mip level in one call) — doesn't require Read/Write
            // Enabled on tex the way a CPU-side GetPixels/SetPixels approach would.
            Graphics.ConvertTexture(tex, 0, arr, i);
        }

        // Graphics.ConvertTexture writes GPU-to-GPU; calling Apply() here would
        // upload the (white-initialized) CPU buffer and overwrite the copied data.
        return arr;
    }
}
