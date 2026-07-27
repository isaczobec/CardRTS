using System;
using UnityEngine;

[Serializable]
public struct TerrainPatchEntry
{
    public string Id;
    public Material Material;
}

// Maps string identifiers to ground-decal materials (assigned in the Inspector — e.g. built
// from MaterialPatchShader/MaterialPatchShaderLarge, which expose a _Tex texture and a
// _Offset Vector2) and knows how to spawn one as a flat plane on top of the world mesh.
// Each spawn gets its own Material instance (mirrors RangeIndicatorPrefab/AoeSpellPrefab's
// "new Material(sharedMaterial)" convention, rather than a shared MaterialPropertyBlock)
// with a randomized _Offset, so spawning the same registry entry at several locations
// doesn't look like an obviously repeated stamp.
public class TerrainPatchRegistry : MonoBehaviour
{
    private const string OffsetProperty = "_Offset";

    public TerrainPatchEntry[] Entries;

    private const float QuadMeshSize = 10f;
    private static Mesh _quadMesh;

    // size is the patch's edge length in world units — the shared quad mesh is authored at
    // QuadMeshSize (10x10, not 1x1 — a plain 1x1 quad's near-zero vertex coordinates were
    // prone to precision/normal issues once scaled way up or down), so this divides down to
    // get the localScale factor instead of mapping 1:1.
    public GameObject Spawn(string id, Vector3 worldPosition, float size)
    {
        Material material = FindMaterial(id);
        if (material == null)
        {
            Debug.LogWarning($"[TerrainPatchRegistry] No entry for id '{id}'.");
            return null;
        }

        var go = new GameObject($"TerrainPatch_{id}");
        go.transform.SetParent(transform, false);
        go.transform.position = worldPosition + new Vector3(0f, 0.01f, 0f);
        go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        float scale = size / QuadMeshSize;
        go.transform.localScale = new Vector3(scale, 1f, scale);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = GetQuadMesh();

        var instance = new Material(material);
        instance.SetVector(OffsetProperty, new Vector4(UnityEngine.Random.value, UnityEngine.Random.value, 0f, 0f));
        mr.material = instance;

        return go;
    }

    private Material FindMaterial(string id)
    {
        if (Entries == null) return null;
        foreach (var entry in Entries)
            if (entry.Id == id)
                return entry.Material;
        return null;
    }

    // A flat QuadMeshSize x QuadMeshSize quad on the XZ plane (normal up), built once and
    // shared across every patch instance — only the material differs between instances, so
    // the mesh itself is safe to share (same "authored at a fixed size, then scaled" idea as
    // RangeIndicatorPrefab/ExpandingCirclePrefab's own "1 unit diameter" convention, just
    // built in code here — at 10 units instead of 1 — since patches are spawned procedurally
    // rather than off a prefab).
    private static Mesh GetQuadMesh()
    {
        if (_quadMesh != null) return _quadMesh;

        float half = QuadMeshSize * 0.5f;
        _quadMesh = new Mesh { name = "TerrainPatchQuad" };
        _quadMesh.vertices = new[]
        {
            new Vector3(-half, 0f, -half),
            new Vector3(-half, 0f,  half),
            new Vector3( half, 0f,  half),
            new Vector3( half, 0f, -half),
        };
        _quadMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f),
        };
        _quadMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        _quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _quadMesh.RecalculateBounds();
        return _quadMesh;
    }
}
