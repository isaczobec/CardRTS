using UnityEditor;
using UnityEngine;

// Grid-toggle inspector for IslandFootprint — the raw bool[] mask is unusable through the
// default inspector (a flat list of 100+ individual bool fields), so this renders it as a
// clickable Width x Height button grid instead, matching what the model actually looks like
// top-down. Combine with IslandFootprint.OnDrawGizmosSelected (visible in the Scene view
// while this object is selected) to paint the mask directly against the model's silhouette.
[CustomEditor(typeof(IslandFootprint))]
public class IslandFootprintEditor : Editor
{
    private const float CellButtonSize = 18f;

    public override void OnInspectorGUI()
    {
        var footprint = (IslandFootprint)target;

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

        EditorGUILayout.Space();

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

                if (GUILayout.Button(GUIContent.none, GUILayout.Width(CellButtonSize), GUILayout.Height(CellButtonSize)))
                {
                    Undo.RecordObject(footprint, "Toggle Island Footprint Cell");
                    footprint.SetOccupied(x, y, !occupied);
                    EditorUtility.SetDirty(footprint);
                }

                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bridge Anchor Points (click to toggle)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Mark cells at the island's edge where a bridge is allowed to touch down. Should normally also be walkable cells above.", UnityEditor.MessageType.None);

        for (int y = footprint.Height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < footprint.Width; x++)
            {
                bool isAnchor = footprint.IsBridgeAnchor(x, y);
                Color prevColor = GUI.backgroundColor;
                GUI.backgroundColor = isAnchor ? new Color(0.4f, 0.8f, 1f) : new Color(0.6f, 0.6f, 0.6f);

                if (GUILayout.Button(GUIContent.none, GUILayout.Width(CellButtonSize), GUILayout.Height(CellButtonSize)))
                {
                    Undo.RecordObject(footprint, "Toggle Bridge Anchor");
                    footprint.ToggleBridgeAnchor(x, y);
                    EditorUtility.SetDirty(footprint);
                }

                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();
        }

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
}
