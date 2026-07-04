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
    private const float PortalHighlightHeightOffset = 0.1f;
    private static readonly Color NormalFillColor = new Color(0.2f, 0.9f, 0.3f, 0.35f);
    private static readonly Color HighlightFillColor = new Color(0.2f, 0.4f, 1f, 0.55f);
    private static readonly Color PortalHighlightColor = new Color(1f, 0.6f, 0f, 1f);

    [SerializeField] private Material _debugMaterial;

    private GameObject _visualizationRoot;

    // Fill mesh kept live (not just built-and-forgotten) so HighlightPath can recolor
    // individual nodes' vertices in place instead of rebuilding the whole mesh.
    private Mesh _fillMesh;
    private List<Color> _fillColors;
    private Dictionary<NavMeshNode, int> _nodeVertexStart;
    private readonly Dictionary<NavMeshNode, float> _highlightExpiry = new();

    // Transient overlay showing the portals a found path crosses, in order. Unlike
    // the per-node fill highlight (which tracks each node's own expiry so overlapping
    // paths don't stomp each other), this is a single throwaway visual per path: a new
    // call just replaces whatever was there before.
    private GameObject _pathPortalsRoot;
    private float? _pathPortalsExpireAt;

    public bool IsVisible => _visualizationRoot != null;

    void Update()
    {
        if (_highlightExpiry.Count > 0)
        {
            List<NavMeshNode> expired = null;
            foreach (var kvp in _highlightExpiry)
            {
                if (Time.time >= kvp.Value)
                    (expired ??= new List<NavMeshNode>()).Add(kvp.Key);
            }

            if (expired != null)
            {
                foreach (var node in expired)
                {
                    _highlightExpiry.Remove(node);
                    SetNodeColor(node, NormalFillColor);
                }
                _fillMesh.SetColors(_fillColors);
            }
        }

        if (_pathPortalsExpireAt.HasValue && Time.time >= _pathPortalsExpireAt.Value)
        {
            Destroy(_pathPortalsRoot);
            _pathPortalsRoot = null;
            _pathPortalsExpireAt = null;
        }
    }

    // Colors a path's nodes blue and draws its crossed portals for durationSeconds,
    // then reverts/clears both. Safe to call even when the visualization is hidden —
    // it's a no-op then, since there's no fill mesh or root to draw into.
    public void HighlightPath(List<NavMeshNode> path, float durationSeconds = 10f)
    {
        if (!IsVisible || path == null || _fillMesh == null)
            return;

        float expireTime = Time.time + durationSeconds;
        foreach (var node in path)
        {
            if (!_nodeVertexStart.ContainsKey(node))
                continue;
            SetNodeColor(node, HighlightFillColor);
            _highlightExpiry[node] = expireTime;
        }
        _fillMesh.SetColors(_fillColors);

        if (_pathPortalsRoot != null)
            Destroy(_pathPortalsRoot);
        _pathPortalsRoot = BuildPortalHighlight(path);
        _pathPortalsExpireAt = _pathPortalsRoot != null ? expireTime : (float?)null;
    }

    private GameObject BuildPortalHighlight(List<NavMeshNode> path)
    {
        if (path.Count < 2)
            return null;

        var handler = WorldManager.instance.Handler;
        int worldSize = WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks;

        var vertices = new List<Vector3>((path.Count - 1) * 2);
        var colors = new List<Color>((path.Count - 1) * 2);
        var lines = new List<int>((path.Count - 1) * 2);

        for (int i = 0; i < path.Count - 1; i++)
        {
            (Vector2 p1, Vector2 p2) = NavMeshHandler.ComputePortalFloat(path[i], path[i + 1]);

            int bl = vertices.Count;
            vertices.Add(CornerPosition(handler, worldSize, Mathf.RoundToInt(p1.x), Mathf.RoundToInt(p1.y)) + Vector3.up * PortalHighlightHeightOffset);
            vertices.Add(CornerPosition(handler, worldSize, Mathf.RoundToInt(p2.x), Mathf.RoundToInt(p2.y)) + Vector3.up * PortalHighlightHeightOffset);
            colors.Add(PortalHighlightColor);
            colors.Add(PortalHighlightColor);
            lines.Add(bl);
            lines.Add(bl + 1);
        }

        var go = new GameObject("PathPortals");
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

        return go;
    }

    private void SetNodeColor(NavMeshNode node, Color color)
    {
        if (!_nodeVertexStart.TryGetValue(node, out int start))
            return;
        for (int i = 0; i < 4; i++)
            _fillColors[start + i] = color;
    }

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

        _fillMesh = null;
        _fillColors = null;
        _nodeVertexStart = null;
        _highlightExpiry.Clear();

        // _pathPortalsRoot is parented under _visualizationRoot, already destroyed above.
        _pathPortalsRoot = null;
        _pathPortalsExpireAt = null;
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
        _nodeVertexStart = new Dictionary<NavMeshNode, int>(nodes.Count);

        foreach (var node in nodes)
        {
            int bl = vertices.Count;
            _nodeVertexStart[node] = bl;
            vertices.Add(CornerPosition(handler, worldSize, node.x1, node.y1));
            vertices.Add(CornerPosition(handler, worldSize, node.x2 + 1, node.y1));
            vertices.Add(CornerPosition(handler, worldSize, node.x1, node.y2 + 1));
            vertices.Add(CornerPosition(handler, worldSize, node.x2 + 1, node.y2 + 1));
            for (int i = 0; i < 4; i++)
                colors.Add(NormalFillColor);

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

        _fillMesh = mesh;
        _fillColors = colors;
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
