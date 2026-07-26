using System;
using UnityEngine;

/// <summary>
/// Generates per-chunk meshes and tile-ID textures from a WorldGenHandler.
/// The mesh covers [0, CHUNK_SIZE_TILES] in XZ with Y driven by the heightmap.
/// The tile-ID texture is R8 format, Point-filtered, one texel per tile corner (or finer,
/// see BuildSmoothedTileTexture); the shader blends between the four surrounding texels
/// and reads the texture's own dimensions, so it works with either resolution unchanged.
/// </summary>
public static class WorldMeshGenerator
{
    // Chunks with more than one tile type get a supersampled + smoothed ID texture instead
    // of the 1-texel-per-tile-corner grid, so boundaries don't trace the tile grid as blocky
    // squares. Uniform chunks (the common case) keep the cheap 1x path untouched.
    private const int ID_SUPERSAMPLE = 4;
    private const float SMOOTH_RADIUS_TILES = 1.25f;

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
    /// Builds the tile-ID texture for one chunk: the cheap (CHUNK_SIZE_TILES+1)^2,
    /// 1-texel-per-tile-corner grid for chunks that are a single tile type throughout
    /// (including their border, which samples one tile into each neighbour so seams
    /// blend correctly), or a supersampled + smoothed grid (see BuildSmoothedTileTexture)
    /// for chunks that straddle a tile-type boundary, so that boundary reads as an
    /// organic curve rather than tracing the tile grid.
    /// </summary>
    public static Texture2D BuildChunkTileTexture(WorldGenHandler handler, ushort chunkX, ushort chunkY)
    {
        int size = WorldGenHandler.CHUNK_SIZE_TILES;
        int worldSize = WorldGenHandler.WorldSizeChunks * size;

        if (ChunkHasMultipleTileTypes(handler, chunkX, chunkY, size, worldSize))
            return BuildSmoothedTileTexture(handler, chunkX, chunkY, size, worldSize);

        return BuildUniformTileTexture(handler, chunkX, chunkY, size, worldSize);
    }

    private static bool ChunkHasMultipleTileTypes(WorldGenHandler handler, ushort chunkX, ushort chunkY, int size, int worldSize)
    {
        int texSize = size + 1;
        TileType? first = null;

        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                ushort wx = (ushort)Mathf.Min(chunkX * size + x, worldSize - 1);
                ushort wy = (ushort)Mathf.Min(chunkY * size + y, worldSize - 1);
                TileType t = handler.GetTileType(wx, wy);

                if (first == null) first = t;
                else if (t != first) return true;
            }
        }
        return false;
    }

    private static Texture2D BuildUniformTileTexture(WorldGenHandler handler, ushort chunkX, ushort chunkY, int size, int worldSize)
    {
        int texSize = size + 1;

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

    /// <summary>
    /// Builds a (CHUNK_SIZE_TILES*ID_SUPERSAMPLE+1)^2 R8 texture. Each fine texel picks the
    /// tile type with the most accumulated weight from nearby actual tiles within
    /// SMOOTH_RADIUS_TILES (linear falloff), i.e. a weighted-mode filter over the categorical
    /// tile grid. This can't be done by blurring the ID values directly — TileType bytes are
    /// categorical, not an interpolatable quantity — so each fine texel still ends up storing
    /// a single real TileType, just chosen from a smoothed vote instead of the single nearest
    /// tile. That's what rounds the boundary's corners off instead of leaving it grid-aligned.
    /// </summary>
    private static Texture2D BuildSmoothedTileTexture(WorldGenHandler handler, ushort chunkX, ushort chunkY, int size, int worldSize)
    {
        int texSize = size * ID_SUPERSAMPLE + 1;

        var texture = new Texture2D(texSize, texSize, TextureFormat.R8, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode   = TextureWrapMode.Clamp;

        var pixels = new byte[texSize * texSize];
        int typeCount = Enum.GetValues(typeof(TileType)).Length;
        var weights = new float[typeCount];
        int kernelRadius = Mathf.CeilToInt(SMOOTH_RADIUS_TILES);

        for (int fy = 0; fy < texSize; fy++)
        {
            for (int fx = 0; fx < texSize; fx++)
            {
                float posX = (float)fx / ID_SUPERSAMPLE;
                float posY = (float)fy / ID_SUPERSAMPLE;

                Array.Clear(weights, 0, weights.Length);

                int centerX = Mathf.RoundToInt(posX);
                int centerY = Mathf.RoundToInt(posY);

                for (int oy = -kernelRadius; oy <= kernelRadius; oy++)
                {
                    for (int ox = -kernelRadius; ox <= kernelRadius; ox++)
                    {
                        int localX = centerX + ox;
                        int localY = centerY + oy;

                        float dx = localX - posX;
                        float dy = localY - posY;
                        float weight = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / SMOOTH_RADIUS_TILES);
                        if (weight <= 0f) continue;

                        ushort wx = (ushort)Mathf.Clamp(chunkX * size + localX, 0, worldSize - 1);
                        ushort wy = (ushort)Mathf.Clamp(chunkY * size + localY, 0, worldSize - 1);
                        weights[(int)handler.GetTileType(wx, wy)] += weight;
                    }
                }

                int bestType = 0;
                float bestWeight = -1f;
                for (int t = 0; t < typeCount; t++)
                {
                    if (weights[t] > bestWeight)
                    {
                        bestWeight = weights[t];
                        bestType = t;
                    }
                }

                pixels[fy * texSize + fx] = (byte)bestType;
            }
        }

        texture.LoadRawTextureData(pixels);
        texture.Apply();
        return texture;
    }
}
