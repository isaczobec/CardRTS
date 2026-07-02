using System;
using UnityEngine;

[Serializable]
public struct TileTextureEntry
{
    public TileType Type;
    public Texture2D Texture;
}

/// <summary>
/// Maps TileType enum values to Texture2Ds via explicit (Type, Texture) pairs assigned in
/// the Inspector. Order does not matter; BuildArray() places each texture at the slot
/// matching its enum value so the terrain shader can index by tile ID.
/// All textures must share the same dimensions and format.
/// </summary>
public class TileTextureRegistry : MonoBehaviour
{
    public TileTextureEntry[] Entries;

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
        var arr = new Texture2DArray(first.width, first.height, count, first.format, useMips);

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
            int mipsToCopy = Mathf.Min(tex.mipmapCount, arr.mipmapCount);
            for (int mip = 0; mip < mipsToCopy; mip++)
                Graphics.CopyTexture(tex, 0, mip, arr, i, mip);
        }

        // Graphics.CopyTexture writes GPU-to-GPU; calling Apply() here would
        // upload the (white-initialized) CPU buffer and overwrite the copied data.
        return arr;
    }
}
