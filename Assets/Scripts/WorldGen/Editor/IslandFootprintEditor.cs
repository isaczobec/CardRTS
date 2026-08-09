using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Grid-toggle inspector for IslandFootprint — the raw bool[] mask is unusable through the
// default inspector (a flat list of 100+ individual bool fields), so this renders it as a
// clickable Width x Height button grid instead, matching what the model actually looks like
// top-down. Combine with IslandFootprint.OnDrawGizmosSelected (visible in the Scene view
// while this object is selected) to paint the mask directly against the model's silhouette.
//
// Two shortcuts on top of plain per-cell clicking: Flood Fill Mode (paint-bucket a whole
// connected region in one click) and Auto-Detect From Colliders (raycast down through the
// model's own colliders to seed a starting mask instead of painting every cell by hand).
[CustomEditor(typeof(IslandFootprint))]
public class IslandFootprintEditor : Editor
{
    // Cell button size shrinks (see GetCellButtonSize) as the grid gets wider, clamped to
    // this range so it never grows past a comfortable click target or shrinks into
    // unusably/invisibly small squares on a very wide island.
    private const float MaxCellButtonSize = 18f;
    private const float MinCellButtonSize = 6f;

    // Subtracted from EditorGUIUtility.currentViewWidth (the standard editor-UI stand-in for
    // "how wide is the panel this GUI is drawing into") to roughly account for the
    // inspector's own left/right padding and scrollbar, so a full-width grid doesn't
    // overflow and wrap.
    private const float InspectorWidthMargin = 40f;

    // Extra clearance (world units) added above the model's collider bounds for the
    // auto-detect ray's start point / total length, so the ray always starts comfortably
    // above the geometry and reaches comfortably below it.
    private const float AutoDetectRayMargin = 5f;

    // Per-editor-instance (not per-footprint) — resets to off whenever a different object is
    // selected, same as any other tool-mode toggle in the Unity editor.
    private bool _floodFillMode;

    // Independent scroll positions for the two grids — GetCellButtonSize keeps most islands
    // fully visible without needing to scroll at all, but once a grid is wide enough to hit
    // MinCellButtonSize, its row width can still exceed the inspector's — these let you pan
    // across it horizontally instead of it just clipping/wrapping.
    private Vector2 _walkableScrollPosition;
    private Vector2 _bridgeAnchorScrollPosition;

    // Minimum spacing (tiles) PlaceBridgeAnchorsAlongEdge keeps between the anchors it
    // places — editor-only tool state, not footprint data, same as _floodFillMode.
    private float _edgeAnchorInterval = 4f;

    public override void OnInspectorGUI()
    {
        var footprint = (IslandFootprint)target;
        float cellSize = GetCellButtonSize(footprint.Width);

        EditorGUI.BeginChangeCheck();
        int newWidth = EditorGUILayout.IntField("Width", footprint.Width);
        int newHeight = EditorGUILayout.IntField("Height", footprint.Height);
        if (EditorGUI.EndChangeCheck() && (newWidth != footprint.Width || newHeight != footprint.Height))
        {
            Undo.RecordObject(footprint, "Resize Island Footprint");
            footprint.Resize(newWidth, newHeight);
            EditorUtility.SetDirty(footprint);
        }

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        Vector2Int anchor = EditorGUILayout.Vector2IntField("Base Anchor (-1,-1 = center)", footprint.BaseAnchor);
        float heightOffset = EditorGUILayout.FloatField("Height Offset", footprint.HeightOffset);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(footprint, "Edit Island Footprint");
            footprint.BaseAnchor = anchor;
            footprint.HeightOffset = heightOffset;
            EditorUtility.SetDirty(footprint);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Walkable Cells (click to toggle)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fill All")) SetAll(footprint, true);
        if (GUILayout.Button("Clear All")) SetAll(footprint, false);
        EditorGUILayout.EndHorizontal();

        _floodFillMode = EditorGUILayout.ToggleLeft("Flood Fill Mode (click a cell to fill/clear its whole connected region)", _floodFillMode);

        EditorGUILayout.Space();
        if (GUILayout.Button("Auto-Detect From Colliders (Raycast)"))
            AutoDetectWalkableCells(footprint);
        EditorGUILayout.HelpBox(
            "Raycasts straight down through this object's own colliders at every grid cell to seed a starting walkable mask, growing Width/Height first if the collider bounds don't already fit. Requires at least one Collider (e.g. Mesh Collider) on the model, and only sees real geometry while the prefab is open (in a scene, or in Prefab Mode) — review the result afterward, especially for overhangs or gaps a straight-down ray can miss.",
            UnityEditor.MessageType.Info);

        EditorGUILayout.Space();

        _walkableScrollPosition = EditorGUILayout.BeginScrollView(_walkableScrollPosition, GUILayout.ExpandWidth(true));

        // Drawn top row first (y = Height-1 down to 0) so the grid reads the way it looks
        // from above in the Scene view, not bottom-up.
        for (int y = footprint.Height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < footprint.Width; x++)
            {
                bool occupied = footprint.IsOccupied(x, y);
                Color prevColor = GUI.backgroundColor;
                GUI.backgroundColor = occupied ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);

                if (GUILayout.Button(GUIContent.none, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                {
                    if (_floodFillMode)
                    {
                        Undo.RecordObject(footprint, "Flood Fill Island Footprint");
                        FloodFill(footprint, x, y);
                    }
                    else
                    {
                        Undo.RecordObject(footprint, "Toggle Island Footprint Cell");
                        footprint.SetOccupied(x, y, !occupied);
                    }
                    EditorUtility.SetDirty(footprint);
                }

                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bridge Anchor Points (click to toggle)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Mark cells at the island's edge where a bridge is allowed to touch down. Should normally also be walkable cells above.", UnityEditor.MessageType.None);

        EditorGUILayout.BeginHorizontal();
        _edgeAnchorInterval = Mathf.Max(1f, EditorGUILayout.FloatField("Edge Anchor Interval", _edgeAnchorInterval));
        if (GUILayout.Button("Place Anchors Along Edge", GUILayout.Width(160f)))
            PlaceBridgeAnchorsAlongEdge(footprint, _edgeAnchorInterval);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("Replaces every current bridge anchor with a fresh ring of points spaced at least Edge Anchor Interval tiles apart around the walkable mask's outer edge — a starting point to review/adjust, not a final answer.", UnityEditor.MessageType.Info);

        _bridgeAnchorScrollPosition = EditorGUILayout.BeginScrollView(_bridgeAnchorScrollPosition, GUILayout.ExpandWidth(true));

        for (int y = footprint.Height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < footprint.Width; x++)
            {
                bool isAnchor = footprint.IsBridgeAnchor(x, y);
                Color prevColor = GUI.backgroundColor;
                GUI.backgroundColor = isAnchor ? new Color(0.4f, 0.8f, 1f) : new Color(0.6f, 0.6f, 0.6f);

                if (GUILayout.Button(GUIContent.none, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                {
                    Undo.RecordObject(footprint, "Toggle Bridge Anchor");
                    footprint.ToggleBridgeAnchor(x, y);
                    EditorUtility.SetDirty(footprint);
                }

                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        SceneView.RepaintAll();
    }

    private void SetAll(IslandFootprint footprint, bool value)
    {
        Undo.RecordObject(footprint, value ? "Fill Island Footprint" : "Clear Island Footprint");
        for (int y = 0; y < footprint.Height; y++)
            for (int x = 0; x < footprint.Width; x++)
                footprint.SetOccupied(x, y, value);
        EditorUtility.SetDirty(footprint);
    }

    // Shrinks the button size as the grid gets wider, so a large island's grid still
    // roughly fits the inspector's own width instead of every row overflowing/wrapping —
    // EditorGUIUtility.currentViewWidth is the standard editor-UI approximation of "how wide
    // is the panel currently being drawn into" (there's no direct API for it), clamped to
    // [MinCellButtonSize, MaxCellButtonSize] so a tiny/huge island doesn't produce invisible
    // or oversized buttons.
    private static float GetCellButtonSize(int gridWidth)
    {
        if (gridWidth <= 0) return MaxCellButtonSize;
        float available = EditorGUIUtility.currentViewWidth - InspectorWidthMargin;
        float size = available / gridWidth;
        return Mathf.Clamp(size, MinCellButtonSize, MaxCellButtonSize);
    }

    // Classic paint-bucket flood fill: every cell 4-connected to (startX, startY) that
    // currently shares its walkable/not-walkable state gets flipped to the opposite state —
    // lets a whole contiguous blob get filled or cleared in one click instead of one cell at
    // a time. Iterative (stack-based) rather than recursive so a large island's flood fill
    // can't blow the call stack.
    private static void FloodFill(IslandFootprint footprint, int startX, int startY)
    {
        bool targetState = footprint.IsOccupied(startX, startY);
        bool fillState = !targetState;

        var stack = new Stack<Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        stack.Push(new Vector2Int(startX, startY));

        while (stack.Count > 0)
        {
            Vector2Int cell = stack.Pop();
            if (!visited.Add(cell)) continue;
            if (cell.x < 0 || cell.y < 0 || cell.x >= footprint.Width || cell.y >= footprint.Height) continue;
            if (footprint.IsOccupied(cell.x, cell.y) != targetState) continue;

            footprint.SetOccupied(cell.x, cell.y, fillState);

            stack.Push(new Vector2Int(cell.x + 1, cell.y));
            stack.Push(new Vector2Int(cell.x - 1, cell.y));
            stack.Push(new Vector2Int(cell.x, cell.y + 1));
            stack.Push(new Vector2Int(cell.x, cell.y - 1));
        }
    }

    // Seeds the walkable mask by raycasting straight down (world -Y) at every grid cell
    // against this object's own colliders only (not a general Physics.Raycast against
    // whatever else happens to be in the scene) — gathered once up front via
    // GetComponentsInChildren, then tested directly via Collider.Raycast, so this neither
    // depends on layers/masks nor risks hitting unrelated geometry. Assumes the object isn't
    // tilted relative to world up, matching the same "islands are never rotated" convention
    // IslandPlayerBaseFeature relies on for placement.
    private static void AutoDetectWalkableCells(IslandFootprint footprint)
    {
        Collider[] colliders = footprint.GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            Debug.LogWarning("[IslandFootprintEditor] No colliders found under this object — add at least one (e.g. a Mesh Collider) before auto-detecting.");
            return;
        }

        Bounds bounds = colliders[0].bounds;
        foreach (Collider col in colliders)
            bounds.Encapsulate(col.bounds);

        float rayStartY = bounds.max.y + AutoDetectRayMargin;
        float rayLength = bounds.size.y + AutoDetectRayMargin * 2f;

        Undo.RecordObject(footprint, "Auto-Detect Island Footprint");

        // Grow the grid to fit the collider bounds if it's currently smaller — never shrink,
        // since an artist may have deliberately padded it beyond the model's own geometry
        // (e.g. for a bridge anchor point sitting just past the mesh's edge).
        int neededWidth = Mathf.Max(footprint.Width, Mathf.CeilToInt(bounds.size.x));
        int neededHeight = Mathf.Max(footprint.Height, Mathf.CeilToInt(bounds.size.z));
        if (neededWidth != footprint.Width || neededHeight != footprint.Height)
            footprint.Resize(neededWidth, neededHeight);

        int hitCount = 0;
        for (int y = 0; y < footprint.Height; y++)
        {
            for (int x = 0; x < footprint.Width; x++)
            {
                Vector3 local = new Vector3(x - footprint.Width / 2f + 0.5f, 0f, y - footprint.Height / 2f + 0.5f);
                Vector3 world = footprint.transform.TransformPoint(local);
                Ray ray = new Ray(new Vector3(world.x, rayStartY, world.z), Vector3.down);

                bool hit = false;
                foreach (Collider col in colliders)
                {
                    if (col != null && col.Raycast(ray, out _, rayLength))
                    {
                        hit = true;
                        break;
                    }
                }

                footprint.SetOccupied(x, y, hit);
                if (hit) hitCount++;
            }
        }

        EditorUtility.SetDirty(footprint);
        Debug.Log($"[IslandFootprintEditor] Auto-detect found {hitCount}/{footprint.Width * footprint.Height} walkable cells.");
    }

    // Replaces BridgeAnchors with a fresh ring of points spaced at least intervalTiles apart
    // around the walkable mask's outer edge — same "seed a starting point, then review" spirit
    // as AutoDetectWalkableCells above. Boundary cells are sorted by angle around the
    // footprint's centroid (a stand-in for "walk the perimeter in order" that holds up fine
    // for the roughly star-convex blob shapes islands actually are — a true contour trace
    // would handle wilder concave/ring shapes better, but isn't worth the complexity here),
    // then kept greedily: a cell only survives if it's far enough from every anchor already
    // kept, which is what actually produces the requested spacing.
    private static void PlaceBridgeAnchorsAlongEdge(IslandFootprint footprint, float intervalTiles)
    {
        List<Vector2Int> boundaryCells = FindBoundaryCells(footprint);
        if (boundaryCells.Count == 0)
        {
            Debug.LogWarning("[IslandFootprintEditor] No walkable cells to trace an edge from — mark some walkable cells (or run Auto-Detect) first.");
            return;
        }

        Vector2 centroid = Vector2.zero;
        foreach (Vector2Int cell in boundaryCells)
            centroid += new Vector2(cell.x, cell.y);
        centroid /= boundaryCells.Count;

        boundaryCells.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - centroid.y, a.x - centroid.x);
            float angleB = Mathf.Atan2(b.y - centroid.y, b.x - centroid.x);
            return angleA.CompareTo(angleB);
        });

        List<Vector2Int> selected = new List<Vector2Int>();
        float minDistSqr = intervalTiles * intervalTiles;

        foreach (Vector2Int cell in boundaryCells)
        {
            bool farEnough = true;
            foreach (Vector2Int existing in selected)
            {
                float dx = cell.x - existing.x, dy = cell.y - existing.y;
                if (dx * dx + dy * dy < minDistSqr)
                {
                    farEnough = false;
                    break;
                }
            }
            if (farEnough) selected.Add(cell);
        }

        Undo.RecordObject(footprint, "Place Bridge Anchors Along Edge");
        footprint.BridgeAnchors.Clear();
        footprint.BridgeAnchors.AddRange(selected);
        EditorUtility.SetDirty(footprint);

        Debug.Log($"[IslandFootprintEditor] Placed {selected.Count} bridge anchor(s) along the edge.");
    }

    // Every occupied cell that borders a non-occupied (or out-of-bounds — IsOccupied already
    // treats those the same) cell in one of the 4 cardinal directions.
    private static List<Vector2Int> FindBoundaryCells(IslandFootprint footprint)
    {
        var result = new List<Vector2Int>();
        for (int y = 0; y < footprint.Height; y++)
        {
            for (int x = 0; x < footprint.Width; x++)
            {
                if (!footprint.IsOccupied(x, y)) continue;

                bool isBoundary = !footprint.IsOccupied(x + 1, y) || !footprint.IsOccupied(x - 1, y) ||
                                   !footprint.IsOccupied(x, y + 1) || !footprint.IsOccupied(x, y - 1);
                if (isBoundary)
                    result.Add(new Vector2Int(x, y));
            }
        }
        return result;
    }
}
