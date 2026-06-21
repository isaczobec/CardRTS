using UnityEngine;

/// <summary>
/// Holds one Texture2D per TileType value (Inspector order = enum value order).
/// Call BuildArray() once at world-render time to produce the Texture2DArray
/// that the terrain shader indexes by tile ID.
/// All textures must share the same dimensions and format.
/// </summary>
public class TileTextureRegistry : MonoBehaviour
{
    [Tooltip("Index 0 = TileType value 0, index 1 = TileType value 1, etc.")]
    public Texture2D[] TileTextures;

    public Texture2DArray BuildArray()
    {
        if (TileTextures == null || TileTextures.Length == 0)
        {
            Debug.LogWarning("[TileTextureRegistry] No textures assigned.");
            return null;
        }

        var first = TileTextures[0];
        bool useMips = first.mipmapCount > 1;
        var arr = new Texture2DArray(first.width, first.height, TileTextures.Length, first.format, useMips);

        for (int i = 0; i < TileTextures.Length; i++)
        {
            var tex = TileTextures[i];
            if (tex.width != first.width || tex.height != first.height)
            {
                Debug.LogError($"[TileTextureRegistry] Texture [{i}] '{tex.name}' is {tex.width}x{tex.height} " +
                               $"but the array expects {first.width}x{first.height}. " +
                               $"Set the same Max Texture Size on all tile textures in their import settings.");
                continue;
            }
            int mipsToCopy = Mathf.Min(tex.mipmapCount, arr.mipmapCount);
            for (int mip = 0; mip < mipsToCopy; mip++)
                Graphics.CopyTexture(tex, 0, mip, arr, i, mip);
        }

        arr.Apply(false, true);
        return arr;
    }
}
