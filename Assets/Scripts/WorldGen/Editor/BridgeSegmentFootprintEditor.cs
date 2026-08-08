using UnityEditor;
using UnityEngine;

// Draws a wireframe box in the Scene view showing exactly what SegmentLength x SegmentWidth
// occupies relative to the prefab's own pivot/orientation — lets you check a prefab's
// declared numbers against its actual mesh before handing it to IslandBridgeFeature. Kept as
// a standalone [DrawGizmo] method (rather than BridgeSegmentFootprint.OnDrawGizmosSelected,
// which is how IslandFootprint's own gizmo works) purely so BridgeSegmentFootprint itself
// stays a plain data component with no editor-only concerns of its own — either approach
// would work fine here since Gizmos calls are already stripped from player builds.
public static class BridgeSegmentFootprintEditor
{
    // The footprint itself has no height field (nothing about placement depends on it) — this
    // is just enough box height to read as a 3D box rather than a flat quad in the Scene view.
    private const float VisualBoxHeight = 1f;

    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    private static void DrawFootprintGizmo(BridgeSegmentFootprint footprint, GizmoType gizmoType)
    {
        // X = SegmentWidth, Z = SegmentLength — matches BridgeSegmentFootprint's own
        // "local +Z is the length axis" convention.
        Vector3 size = new Vector3(footprint.SegmentWidth, VisualBoxHeight, footprint.SegmentLength);
        // Centered on the transform, per the "pivot is the segment's center" convention —
        // shifted up by HeightOffset so the box also shows where placement will actually put
        // the prefab relative to wherever it's parked in the scene right now.
        Vector3 center = new Vector3(0f, footprint.HeightOffset + VisualBoxHeight * 0.5f, 0f);

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = footprint.transform.localToWorldMatrix;

        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.25f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, size);

        Gizmos.matrix = previousMatrix;
    }
}
