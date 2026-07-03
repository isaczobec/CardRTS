using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Debug overlay for the NavMeshHandler node graph: translucent quads over every
/// walkable node plus a wireframe outline of each node's rectangle. Built fresh
/// each time it's shown and fully torn down when hidden, so it holds no GPU
/// resources while not in use.
/// </summary>
public class NavMeshVisualizer : Singleton<NavMeshVisualizer>
{
    private const float HeightOffset = 0.05f;

    [SerializeField] private Material _debugMaterial;

    private GameObject _visualizationRoot;

    public bool IsVisible => _visualizationRoot != null;

    protected override void Awake()
    {
        base.Awake();
        if (_debugMaterial == null)
            _debugMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    void Start()
    {
        DevConsole.RegisterCommand(
            "toggle-navmesh",
            "Toggles the navmesh debug visualization. Rebuilds it from the current NavMeshHandler state each time it's shown, and discards it when hidden.",
            (info) =>
            {
                Toggle();
                return DevCommandResult.Success($"Navmesh visualization {(IsVisible ? "shown" : "hidden")}.");
            });
    }

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Show();
    }

    public void Show()
    {
        // Always rebuild from scratch rather than reuse a stale mesh from a previous world.
        Hide();

        var handler = WorldManager.instance.Handler;
        var nodes = NavMeshHandler.instance.Nodes;
        if (handler == null || nodes.Count == 0)
            return;

        int worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;

        _visualizationRoot = new GameObject("NavMeshVisualization");
        _visualizationRoot.transform.SetParent(WorldManager.instance.Renderer.transform, false);

        BuildFillMesh(handler, nodes, worldSize);
        BuildWireframeMesh(handler, nodes, worldSize);
    }

    public void Hide()
    {
        if (_visualizationRoot != null)
        {
            Destroy(_visualizationRoot);
            _visualizationRoot = null;
        }
    }

    // Mirrors WorldMeshGenerator's edge handling: the position uses the true tile
    // coordinate (which can be one past the last valid tile at world edges), but the
    // height sample is clamped so it never indexes past the heightmap.
    private static Vector3 CornerPosition(WorldGenHandler handler, int worldSize, int tileX, int tileY)
    {
        ushort hx = (ushort)Mathf.Min(tileX, worldSize - 1);
        ushort hy = (ushort)Mathf.Min(tileY, worldSize - 1);
        float h = handler.GetHeight(hx, hy);
        return new Vector3(tileX, h + HeightOffset, tileY);
    }

    private void BuildFillMesh(WorldGenHandler handler, List<NavMeshNode> nodes, int worldSize)
    {
        var vertices = new List<Vector3>(nodes.Count * 4);
        var colors = new List<Color>(nodes.Count * 4);
        var triangles = new List<int>(nodes.Count * 6);

        var fillColor = new Color(0.2f, 0.9f, 0.3f, 0.35f);

        foreach (var node in nodes)
        {
            int bl = vertices.Count;
            vertices.Add(CornerPosition(handler, worldSize, node.x1, node.y1));
            vertices.Add(CornerPosition(handler, worldSize, node.x2 + 1, node.y1));
            vertices.Add(CornerPosition(handler, worldSize, node.x1, node.y2 + 1));
            vertices.Add(CornerPosition(handler, worldSize, node.x2 + 1, node.y2 + 1));
            for (int i = 0; i < 4; i++)
                colors.Add(fillColor);

            triangles.Add(bl); triangles.Add(bl + 2); triangles.Add(bl + 3);
            triangles.Add(bl); triangles.Add(bl + 3); triangles.Add(bl + 1);
        }

        var go = new GameObject("Fill");
        go.transform.SetParent(_visualizationRoot.transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _debugMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;
    }

    private void BuildWireframeMesh(WorldGenHandler handler, List<NavMeshNode> nodes, int worldSize)
    {
        var vertices = new List<Vector3>(nodes.Count * 4);
        var colors = new List<Color>(nodes.Count * 4);
        var lines = new List<int>(nodes.Count * 8);

        var lineColor = Color.white;

        foreach (var node in nodes)
        {
            int bl = vertices.Count;
            vertices.Add(CornerPosition(handler, worldSize, node.x1, node.y1));
            vertices.Add(CornerPosition(handler, worldSize, node.x2 + 1, node.y1));
            vertices.Add(CornerPosition(handler, worldSize, node.x2 + 1, node.y2 + 1));
            vertices.Add(CornerPosition(handler, worldSize, node.x1, node.y2 + 1));
            for (int i = 0; i < 4; i++)
                colors.Add(lineColor);

            lines.Add(bl);     lines.Add(bl + 1);
            lines.Add(bl + 1); lines.Add(bl + 2);
            lines.Add(bl + 2); lines.Add(bl + 3);
            lines.Add(bl + 3); lines.Add(bl);
        }

        var go = new GameObject("Wireframe");
        go.transform.SetParent(_visualizationRoot.transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _debugMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetIndices(lines, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;
    }
}
