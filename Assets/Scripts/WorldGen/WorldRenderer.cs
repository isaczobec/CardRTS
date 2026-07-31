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

    [SerializeField] private RenderingLayerMask _terrainRenderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask;
    public RenderingLayerMask TerrainRenderingLayerMask => _terrainRenderingLayerMask;

    private readonly Dictionary<(int cx, int cy), GameObject> _chunkObjects = new();

    // Read by WorldChunkVisibilityManager to know which chunk GameObject to toggle for a
    // given chunk coordinate — see that class for why chunks are enabled/disabled instead
    // of relying solely on Unity's own per-renderer frustum culling (shadow-caster passes
    // don't respect the main camera's frustum, so an off-screen chunk can still cost GPU
    // time rendering into a directional light's shadow map unless it's fully disabled).
    public IReadOnlyDictionary<(int cx, int cy), GameObject> ChunkObjects => _chunkObjects;

    [SerializeField]
    private GoTileManager _goTileManager;

    [SerializeField]
    private TerrainPatchRegistry _terrainPatchRegistry;
    public TerrainPatchRegistry TerrainPatchRegistry => _terrainPatchRegistry;

    public void Render(WorldGenHandler handler)
    {
        Clear();

        var tileArray = TextureRegistry.BuildArray();
        if (tileArray != null)
            ChunkMaterial.SetTexture("_TileTextures", tileArray);

        for (ushort cy = 0; cy < WorldGenHandler.WorldSizeChunks; cy++)
            for (ushort cx = 0; cx < WorldGenHandler.WorldSizeChunks; cx++)
                SpawnChunk(handler, cx, cy);
        
        foreach (Chunk c in handler.IterateChunks())
        {
            foreach ((ushort tx, ushort ty) in c.IterateWorldTiles())
            {
                _goTileManager.TrySpawnGoTile(tx, ty);
            }
        }
    }

    void SpawnChunk(WorldGenHandler handler, ushort cx, ushort cy)
    {
        var go = new GameObject($"Chunk_{cx}_{cy}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = WorldManager.instance.TileToWorldPosition(
            (ushort)(cx * WorldGenHandler.CHUNK_SIZE_TILES),
            (ushort)(cy * WorldGenHandler.CHUNK_SIZE_TILES),
            useHeightmap: false);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        mf.sharedMesh    = WorldMeshGenerator.BuildChunkMesh(handler, cx, cy);
        mr.sharedMaterial = ChunkMaterial;
        mr.renderingLayerMask = _terrainRenderingLayerMask;

        var mpb = new MaterialPropertyBlock();
        mpb.SetTexture("_TileIDTex", WorldMeshGenerator.BuildChunkTileTexture(handler, cx, cy));
        mr.SetPropertyBlock(mpb);

        _chunkObjects[(cx, cy)] = go;
    }

    void Clear()
    {
        foreach (var go in _chunkObjects.Values)
            Destroy(go);
        _chunkObjects.Clear();
    }
}
