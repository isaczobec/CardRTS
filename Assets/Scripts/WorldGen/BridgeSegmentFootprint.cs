using UnityEngine;

// Authoring component for a straight modular bridge segment prefab — tells
// IslandBridgeFeature the prefab's real-world size, since a straight segment's own mesh
// bounds aren't otherwise knowable from generation code. Unlike IslandFootprint, no painted
// grid is needed here: a straight segment's occupied tiles are just a plain
// SegmentLength x SegmentWidth rectangle, not an arbitrary shape, so a handful of numbers
// is enough to place and space instances along a curve.
//
// Orientation convention: the prefab's local +Z (forward) axis is its length axis, and its
// pivot is the CENTER of the segment (spanning -SegmentLength/2..+SegmentLength/2 on Z and
// -SegmentWidth/2..+SegmentWidth/2 on X) — IslandBridgeFeature places each instance at the
// midpoint of its own SegmentLength-long stretch of curve, not one end of it, and rotates it
// so +Z follows the curve's tangent there. See BridgeSegmentFootprintEditor for a gizmo that
// draws this box so it's easy to check a prefab's declared numbers against its actual mesh.
public class BridgeSegmentFootprint : MonoBehaviour
{
    // World units the prefab spans along its own local +Z axis.
    public float SegmentLength = 2f;

    // World units the prefab spans left-to-right (local X) — determines how many tiles wide
    // of TileType.Bridge get stamped around the curve.
    public float SegmentWidth = 3f;

    // Added to each placed segment's world Y position, same purpose as
    // IslandFootprint.HeightOffset.
    public float HeightOffset = 0f;
}
