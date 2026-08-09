using System.Collections.Generic;
using UnityEngine;

// A per-placement, grid-aligned view of an IslandFootprint's walkable mask and anchor points
// after being rotated by some angle around the world's Y (up) axis — see FootprintRotator.Rotate
// for how it's built. World gen has no concept of a tile that isn't axis-aligned with the rest
// of the grid, so a "rotated island" doesn't rotate its stamped tiles in place — it resamples an
// entirely new (typically larger, to still fully contain the rotated shape) axis-aligned mask,
// the same way rotating a bitmap image works.
//
// Deliberately never mutates the source IslandFootprint: that component lives on the shared
// prefab asset referenced by IslandRegistry, and the exact same instance is reused for every
// placement of that island type — two gem islands facing different directions both read the
// same IslandFootprint, so each placement needs its own independent, disposable
// RotatedIslandFootprint rather than writing rotated data back onto the shared source.
public class RotatedIslandFootprint
{
    public readonly int Width;
    public readonly int Height;
    public readonly float RotationDegrees;

    private readonly bool[] _occupied;
    private readonly List<Vector2Int> _bridgeAnchors;
    private readonly Vector2Int _baseAnchor;

    public IReadOnlyList<Vector2Int> BridgeAnchors => _bridgeAnchors;

    public RotatedIslandFootprint(int width, int height, bool[] occupied, List<Vector2Int> bridgeAnchors, Vector2Int baseAnchor, float rotationDegrees)
    {
        Width = width;
        Height = height;
        _occupied = occupied;
        _bridgeAnchors = bridgeAnchors;
        _baseAnchor = baseAnchor;
        RotationDegrees = rotationDegrees;
    }

    public bool IsOccupied(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        return _occupied[y * Width + x];
    }

    // Mirrors IslandFootprint.OccupiedCells — every occupied cell in this (already rotated)
    // local grid.
    public IEnumerable<Vector2Int> OccupiedCells()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (IsOccupied(x, y))
                    yield return new Vector2Int(x, y);
    }

    // Mirrors IslandFootprint.BaseAnchorOrDefault — (-1, -1) still means "use the grid's own
    // center cell", same sentinel convention, just against this grid's own (possibly larger)
    // Width/Height.
    public Vector2Int BaseAnchorOrDefault()
        => _baseAnchor.x >= 0 && _baseAnchor.y >= 0 ? _baseAnchor : new Vector2Int(Width / 2, Height / 2);

    // Mirrors IslandFootprint.AnchorWorldPosition — converts one of this rotated grid's own
    // local cells into world tile-space, given the island's placed origin.
    public Vector2 AnchorWorldPosition(Vector2Int localCell, int originX, int originY)
        => new Vector2(originX + localCell.x + 0.5f, originY + localCell.y + 0.5f);
}

// Builds a RotatedIslandFootprint from a source IslandFootprint plus a rotation angle around the
// world's Y (up) axis — the exact same angle the island's visual prefab is instantiated with
// (see IslandPlacementHelper.TryPlaceIsland, which threads one rotationDegrees value through to
// both this and IslandSpawnAction.Rotation), so the resampled walkable tiles and bridge anchors
// always agree with however the mesh itself actually ends up facing.
public static class FootprintRotator
{
    // Multiples of 360 degrees are treated as "no rotation" and take a cheap direct-copy path
    // (Identity) rather than round-tripping through the resampling math below — by far the most
    // common case, since every island type except GemIslandFeature's is currently placed
    // unrotated.
    public static RotatedIslandFootprint Rotate(IslandFootprint footprint, float rotationDegrees)
    {
        if (Mathf.Approximately(Mathf.Repeat(rotationDegrees, 360f), 0f))
            return Identity(footprint);

        Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);
        Quaternion inverse = Quaternion.Euler(0f, -rotationDegrees, 0f);

        float pivotX = footprint.Width * 0.5f;
        float pivotY = footprint.Height * 0.5f;

        // Bounding box of the rotated Width x Height rectangle, in pivot-relative tile units —
        // every corner of the original rectangle, rotated, gives the new (larger, for any
        // non-90-degree-multiple angle) extent the resampled grid needs to fully contain the
        // rotated shape without clipping any corner of it.
        float maxExtentX = 0f, maxExtentY = 0f;
        foreach (Vector2 corner in CornersOf(pivotX, pivotY))
        {
            Vector2 rotated = RotateOffset(corner, rotation);
            maxExtentX = Mathf.Max(maxExtentX, Mathf.Abs(rotated.x));
            maxExtentY = Mathf.Max(maxExtentY, Mathf.Abs(rotated.y));
        }

        // Even dimensions keep the pivot sitting exactly on a grid vertex at (newWidth/2,
        // newHeight/2) — the same convention IslandFootprint's own doc comment describes for
        // the source grid — rather than drifting the pivot off-center for an odd-sized result.
        int newWidth = Mathf.Max(2, Mathf.CeilToInt(maxExtentX) * 2);
        int newHeight = Mathf.Max(2, Mathf.CeilToInt(maxExtentY) * 2);
        float newPivotX = newWidth * 0.5f;
        float newPivotY = newHeight * 0.5f;

        var occupied = new bool[newWidth * newHeight];
        for (int ny = 0; ny < newHeight; ny++)
        {
            for (int nx = 0; nx < newWidth; nx++)
            {
                // Inverse-mapped from the new grid back into the source footprint's own
                // (unrotated) space, rather than forward-mapping each source cell into the new
                // grid — forward-mapping a discrete grid through a rotation can leave gaps
                // (multiple source cells landing on the same destination cell, others landing
                // on none); sampling backward from every destination cell instead guarantees
                // every new cell gets an answer.
                Vector2 newOffset = new Vector2(nx + 0.5f - newPivotX, ny + 0.5f - newPivotY);
                Vector2 originalOffset = RotateOffset(newOffset, inverse);

                int origX = Mathf.FloorToInt(originalOffset.x + pivotX);
                int origY = Mathf.FloorToInt(originalOffset.y + pivotY);
                occupied[ny * newWidth + nx] = footprint.IsOccupied(origX, origY);
            }
        }

        var bridgeAnchors = new List<Vector2Int>(footprint.BridgeAnchors.Count);
        foreach (Vector2Int cell in footprint.BridgeAnchors)
            bridgeAnchors.Add(RotateCell(cell, pivotX, pivotY, newPivotX, newPivotY, rotation));

        Vector2Int baseAnchor = footprint.BaseAnchor.x >= 0 && footprint.BaseAnchor.y >= 0
            ? RotateCell(footprint.BaseAnchor, pivotX, pivotY, newPivotX, newPivotY, rotation)
            : footprint.BaseAnchor;

        return new RotatedIslandFootprint(newWidth, newHeight, occupied, bridgeAnchors, baseAnchor, rotationDegrees);
    }

    // No-rotation fast path — same shape/anchors as the source footprint, just copied into the
    // same RotatedIslandFootprint type every consumer already reads through, so nothing
    // downstream needs to special-case "was this particular island actually rotated?" itself.
    private static RotatedIslandFootprint Identity(IslandFootprint footprint)
    {
        var occupied = new bool[footprint.Width * footprint.Height];
        int i = 0;
        for (int y = 0; y < footprint.Height; y++)
            for (int x = 0; x < footprint.Width; x++)
                occupied[i++] = footprint.IsOccupied(x, y);

        return new RotatedIslandFootprint(footprint.Width, footprint.Height, occupied, new List<Vector2Int>(footprint.BridgeAnchors), footprint.BaseAnchor, 0f);
    }

    private static IEnumerable<Vector2> CornersOf(float halfWidth, float halfHeight)
    {
        yield return new Vector2(-halfWidth, -halfHeight);
        yield return new Vector2(halfWidth, -halfHeight);
        yield return new Vector2(-halfWidth, halfHeight);
        yield return new Vector2(halfWidth, halfHeight);
    }

    private static Vector2Int RotateCell(Vector2Int cell, float pivotX, float pivotY, float newPivotX, float newPivotY, Quaternion rotation)
    {
        Vector2 offset = new Vector2(cell.x + 0.5f - pivotX, cell.y + 0.5f - pivotY);
        Vector2 rotated = RotateOffset(offset, rotation);
        int nx = Mathf.FloorToInt(rotated.x + newPivotX);
        int ny = Mathf.FloorToInt(rotated.y + newPivotY);
        return new Vector2Int(nx, ny);
    }

    // Treats a tile-space (x, y) offset as world (x, z) — matching every other tile/world
    // conversion in world gen (local Y maps to world Z, see WorldManager.TileToWorldPosition) —
    // and rotates it with an actual Unity Quaternion rather than a hand-derived trig formula, so
    // this can never quietly disagree with however IslandSpawnAction.Rotation ends up rotating
    // the island's own visual mesh (both are built from the exact same rotationDegrees value via
    // the exact same Quaternion.Euler call).
    private static Vector2 RotateOffset(Vector2 offset, Quaternion rotation)
    {
        Vector3 rotated = rotation * new Vector3(offset.x, 0f, offset.y);
        return new Vector2(rotated.x, rotated.z);
    }
}
