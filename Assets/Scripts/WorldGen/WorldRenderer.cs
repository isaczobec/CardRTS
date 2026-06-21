using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one GameObject per chunk, each with a MeshFilter + MeshRenderer.
/// The shared material receives the Texture2DArray; each chunk's MeshRenderer
/// gets its own tile-ID texture via a MaterialPropertyBlock.
/// </summary>
public class WorldRenderer : MonoBehaviour
{
    public Material ChunkMaterial;
    public TileTextureRegistry TextureRegistry;

    private readonly List<GameObject> _chunkObjects = new();

    public void Render(WorldGenHandler handler)
    {
        Clear();

        var tileArray = TextureRegistry.BuildArray();
        if (tileArray != null)
            ChunkMaterial.SetTexture("_TileTextures", tileArray);

        for (ushort cy = 0; cy < WorldGenHandler.WorldSizeChunks; cy++)
            for (ushort cx = 0; cx < WorldGenHandler.WorldSizeChunks; cx++)
                SpawnChunk(handler, cx, cy);
    }

    void SpawnChunk(WorldGenHandler handler, ushort cx, ushort cy)
    {
        var go = new GameObject($"Chunk_{cx}_{cy}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(
            cx * WorldGenHandler.CHUNK_SIZE_TILES,
            0f,
            cy * WorldGenHandler.CHUNK_SIZE_TILES);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        mf.sharedMesh    = WorldMeshGenerator.BuildChunkMesh(handler, cx, cy);
        mr.sharedMaterial = ChunkMaterial;

        var mpb = new MaterialPropertyBlock();
        mpb.SetTexture("_TileIDTex", WorldMeshGenerator.BuildChunkTileTexture(handler, cx, cy));
        mr.SetPropertyBlock(mpb);

        _chunkObjects.Add(go);
    }

    void Clear()
    {
        foreach (var go in _chunkObjects)
            Destroy(go);
        _chunkObjects.Clear();
    }
}
