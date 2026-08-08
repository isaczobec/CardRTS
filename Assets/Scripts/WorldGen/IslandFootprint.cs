using System.Collections.Generic;
using UnityEngine;

// Authoring component for an island prefab: defines, in tile-grid space, which cells under
// the model are walkable ground (stamped as TileType.Island by IslandPlayerBaseFeature) and
// which cell a player base should be centered on. Paint the mask with the custom inspector
// (see IslandFootprintEditor) while comparing against the model in the Scene view (this
// component draws a gizmo grid over it when selected).
//
// Grid convention: local cell (0,0) is the grid's bottom-left corner; the prefab's own
// transform (wherever its pivot is) is treated as the CENTER of the grid, i.e. cell
// (Width/2, Height/2) — so the prefab's pivot should sit at (or close to) the model's
// horizontal center for placement to line up with the stamped tiles. If a source model's
// pivot isn't centered, nest it under an empty parent and put this component on the parent
// instead of the model itself.
public class IslandFootprint : MonoBehaviour
{
    public int Width = 10;
    public int Height = 10;

    [SerializeField] private bool[] _occupied = new bool[100];

    // Local cell (relative to this footprint's own grid, not world tiles) a base should be
    // centered on. (-1, -1) means "use the grid's center cell" — see BaseAnchorOrDefault.
    public Vector2Int BaseAnchor = new Vector2Int(-1, -1);

    // Local cells marked (via IslandFootprintEditor) as valid places for a bridge to touch
    // down — typically points right at the island's edge, facing open water/void. Read by
    // IslandBridgeFeature, which picks whichever of these sits closest to the island it's
    // connecting to. Empty is valid (falls back to BaseAnchorOrDefault, with a warning).
    public List<Vector2Int> BridgeAnchors = new List<Vector2Int>();

    // Added to the placed island's world Y position — lets an island prefab float above (or
    // below) the default y=0 plane. Different island variants can use different values.
    public float HeightOffset = 0f;

    public bool IsOccupied(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
        return _occupied[y * Width + x];
    }

    public void SetOccupied(int x, int y, bool value)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        _occupied[y * Width + x] = value;
    }

    public Vector2Int BaseAnchorOrDefault()
        => BaseAnchor.x >= 0 && BaseAnchor.y >= 0 ? BaseAnchor : new Vector2Int(Width / 2, Height / 2);

    public bool IsBridgeAnchor(int x, int y) => BridgeAnchors.Contains(new Vector2Int(x, y));

    // Toggles a cell's BridgeAnchors membership — called from IslandFootprintEditor's own
    // toggle-grid, mirroring SetOccupied's role for the walkable mask.
    public void ToggleBridgeAnchor(int x, int y)
    {
        Vector2Int cell = new Vector2Int(x, y);
        if (!BridgeAnchors.Remove(cell))
            BridgeAnchors.Add(cell);
    }

    // Converts a local cell (e.g. one from BridgeAnchors) into a world tile-space point —
    // the center of that tile — given this island's placed origin (its bottom-left world
    // tile, see IslandPlayerBaseFeature.IslandPlacement.OriginX/Y).
    public Vector2 AnchorWorldPosition(Vector2Int localCell, int originX, int originY)
        => new Vector2(originX + localCell.x + 0.5f, originY + localCell.y + 0.5f);

    // Resizes the mask in place, preserving existing values wherever the old and new bounds
    // overlap — called from IslandFootprintEditor when Width/Height change so painted data
    // survives a resize instead of resetting to empty.
    public void Resize(int newWidth, int newHeight)
    {
        newWidth = Mathf.Max(1, newWidth);
        newHeight = Mathf.Max(1, newHeight);
        bool[] newOccupied = new bool[newWidth * newHeight];

        for (int y = 0; y < Mathf.Min(Height, newHeight); y++)
            for (int x = 0; x < Mathf.Min(Width, newWidth); x++)
                newOccupied[y * newWidth + x] = IsOccupied(x, y);

        Width = newWidth;
        Height = newHeight;
        _occupied = newOccupied;

        // Drop any bridge anchor the shrink just pushed out of bounds — an anchor outside
        // the grid makes no sense and AnchorWorldPosition doesn't validate against bounds.
        BridgeAnchors.RemoveAll(cell => cell.x < 0 || cell.y < 0 || cell.x >= Width || cell.y >= Height);
    }

    // Every occupied local cell — enumerated by IslandPlayerBaseFeature when stamping tiles.
    public IEnumerable<Vector2Int> OccupiedCells()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (IsOccupied(x, y))
                    yield return new Vector2Int(x, y);
    }

    // Visual-only reference grid so the mask can be painted (via IslandFootprintEditor)
    // while looking at the actual model. Assumes the prefab is unscaled (1 tile = 1 world
    // unit) — a scaled-up island will show a mismatched gizmo grid, though the stamped tiles
    // themselves are unaffected.
    private void OnDrawGizmosSelected()
    {
        if (_occupied == null) return;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector3 local = new Vector3(x - Width / 2f + 0.5f, 0f, y - Height / 2f + 0.5f);
                Vector3 world = transform.TransformPoint(local);
                bool occupied = IsOccupied(x, y);

                Gizmos.color = occupied ? new Color(0.2f, 1f, 0.2f, 0.5f) : new Color(1f, 0.2f, 0.2f, 0.15f);
                Gizmos.DrawCube(world, Vector3.one * 0.9f);
                Gizmos.color = occupied ? Color.green : Color.red;
                Gizmos.DrawWireCube(world, Vector3.one * 0.9f);
            }
        }

        Vector2Int anchor = BaseAnchorOrDefault();
        Vector3 anchorLocal = new Vector3(anchor.x - Width / 2f + 0.5f, 0.1f, anchor.y - Height / 2f + 0.5f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.TransformPoint(anchorLocal), 0.4f);

        if (BridgeAnchors != null)
        {
            Gizmos.color = Color.cyan;
            foreach (Vector2Int bridgeAnchor in BridgeAnchors)
            {
                Vector3 bridgeLocal = new Vector3(bridgeAnchor.x - Width / 2f + 0.5f, 0.15f, bridgeAnchor.y - Height / 2f + 0.5f);
                Gizmos.DrawSphere(transform.TransformPoint(bridgeLocal), 0.3f);
            }
        }
    }
}
