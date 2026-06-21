using UnityEngine;

/// <summary>
/// Generates per-chunk meshes and tile-ID textures from a WorldGenHandler.
/// The mesh covers [0, CHUNK_SIZE_TILES] in XZ with Y driven by the heightmap.
/// The tile-ID texture is (CHUNK_SIZE_TILES+1)^2 pixels, R8 format, Point-filtered;
/// the shader is expected to bilinearly blend between the four surrounding tile types.
/// </summary>
public static class WorldMeshGenerator
{
    public static Mesh BuildChunkMesh(WorldGenHandler handler, ushort chunkX, ushort chunkY)
    {
        int size = WorldGenHandler.CHUNK_SIZE_TILES;
        int stride = size + 1;
        int worldSize = WorldGenHandler.WorldSizeChunks * size;

        var vertices  = new Vector3[stride * stride];
        var uvs       = new Vector2[stride * stride];
        var triangles = new int[size * size * 6];

        float invSize = 1f / size;

        for (int y = 0; y < stride; y++)
        {
            for (int x = 0; x < stride; x++)
            {
                // Clamp to world bounds for the last row/column of each edge chunk.
                ushort wx = (ushort)Mathf.Min(chunkX * size + x, worldSize - 1);
                ushort wy = (ushort)Mathf.Min(chunkY * size + y, worldSize - 1);

                float h = handler.GetHeight(wx, wy);
                int vi = y * stride + x;
                vertices[vi] = new Vector3(x, h, y);
                uvs[vi]      = new Vector2(x * invSize, y * invSize);
            }
        }

        int ti = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int bl = y * stride + x;
                int br = bl + 1;
                int tl = bl + stride;
                int tr = tl + 1;

                triangles[ti++] = bl;
                triangles[ti++] = tl;
                triangles[ti++] = tr;

                triangles[ti++] = bl;
                triangles[ti++] = tr;
                triangles[ti++] = br;
            }
        }

        var mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Builds a (CHUNK_SIZE_TILES+1) x (CHUNK_SIZE_TILES+1) R8 texture where each
    /// texel stores the TileType byte of the corresponding world tile. The +1 border
    /// captures one tile from each neighbouring chunk so the shader can blend across
    /// chunk seams without extra logic.
    /// </summary>
    public static Texture2D BuildChunkTileTexture(WorldGenHandler handler, ushort chunkX, ushort chunkY)
    {
        int size    = WorldGenHandler.CHUNK_SIZE_TILES;
        int texSize = size + 1;
        int worldSize = WorldGenHandler.WorldSizeChunks * size;

        var texture = new Texture2D(texSize, texSize, TextureFormat.R8, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode   = TextureWrapMode.Clamp;

        var pixels = new byte[texSize * texSize];

        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                ushort wx = (ushort)Mathf.Min(chunkX * size + x, worldSize - 1);
                ushort wy = (ushort)Mathf.Min(chunkY * size + y, worldSize - 1);
                pixels[y * texSize + x] = (byte)handler.GetTileType(wx, wy);
            }
        }

        texture.LoadRawTextureData(pixels);
        texture.Apply();
        return texture;
    }
}
