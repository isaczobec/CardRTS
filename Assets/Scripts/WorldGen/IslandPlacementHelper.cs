using UnityEngine;

// Shared "stamp a footprint as TileType.Island and enqueue its visual spawn" logic, used by
// every feature that places an island — IslandPlayerBaseFeature (player bases),
// MidIslandFeature (the central hub island), and IslandBridgeFeature (intermittent bridge
// waypoint islands) — so the actual placement mechanics live in exactly one place regardless
// of which feature places an island or how many of a given kind get placed.
public static class IslandPlacementHelper
{
    // A placed island's tile-grid footprint, independent of which feature placed it or why
    // (a player base, the mid-map hub, or a bridge waypoint all look the same from here —
    // any feature-specific bookkeeping, like IslandPlayerBaseFeature tracking a ClientId,
    // wraps this rather than duplicating its fields).
    public readonly struct PlacedIsland
    {
        public readonly ushort OriginX;
        public readonly ushort OriginY;
        public readonly IslandFootprint Footprint;

        public PlacedIsland(ushort originX, ushort originY, IslandFootprint footprint)
        {
            OriginX = originX;
            OriginY = originY;
            Footprint = footprint;
        }

        // Approximate world-space center of the footprint's bounding box — used by
        // IslandBridgeFeature both to pick which BridgeAnchors point faces another island
        // and to lay out a bridge's overall curve. Not tile-clamped (an island right at the
        // map edge could nominally center slightly outside it), which is fine here since
        // this is only ever used as a direction/distance reference point, never a tile
        // written to.
        public Vector2 CenterWorldPosition => new Vector2(OriginX + Footprint.Width / 2f, OriginY + Footprint.Height / 2f);
    }

    // Stamps prefab's IslandFootprint as TileType.Island centered on (centerTileX,
    // centerTileY) — clamped to the world's actual bounds first, see below — and enqueues an
    // IslandSpawnAction to instantiate it. Returns null (and stamps/spawns nothing) if prefab
    // has no IslandFootprint component.
    public static PlacedIsland? TryPlaceIsland(WorldGenHandler handler, GameObject prefab, int centerTileX, int centerTileY, ushort worldSize)
    {
        IslandFootprint footprint = prefab != null ? prefab.GetComponent<IslandFootprint>() : null;
        if (footprint == null) return null;

        // Clamped BEFORE computing origin (rather than clamping origin separately afterward,
        // as this used to do) so the footprint actually stamped, the visual spawn position,
        // and the returned PlacedIsland.OriginX/Y all agree on where this island ended up —
        // a caller (e.g. IslandBridgeFeature placing a waypoint island along a bowed curve)
        // handing this a wildly out-of-bounds center used to stamp tiles from one (unclamped)
        // origin while everything else reported a different (clamped) one. A large footprint
        // clamped right up against an edge can still end up with part of itself off-map —
        // StampFootprint already skips any such cell — which is expected: the island's
        // POSITION is clamped to the map, not necessarily its whole extent.
        int clampedCenterX = Mathf.Clamp(centerTileX, 0, worldSize - 1);
        int clampedCenterY = Mathf.Clamp(centerTileY, 0, worldSize - 1);

        int originX = clampedCenterX - footprint.Width / 2;
        int originY = clampedCenterY - footprint.Height / 2;

        StampFootprint(handler, footprint, originX, originY, worldSize);

        Vector3 worldPosition = WorldManager.instance.TileToWorldPosition((ushort)clampedCenterX, (ushort)clampedCenterY, center: true);
        worldPosition.y += footprint.HeightOffset;
        handler.EnqueueAction(new IslandSpawnAction { Prefab = prefab, WorldPosition = worldPosition });

        ushort clampedOriginX = (ushort)Mathf.Clamp(originX, 0, worldSize - 1);
        ushort clampedOriginY = (ushort)Mathf.Clamp(originY, 0, worldSize - 1);

        return new PlacedIsland(clampedOriginX, clampedOriginY, footprint);
    }

    private static void StampFootprint(WorldGenHandler handler, IslandFootprint footprint, int originX, int originY, ushort worldSize)
    {
        foreach (Vector2Int cell in footprint.OccupiedCells())
        {
            int tx = originX + cell.x;
            int ty = originY + cell.y;
            if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
            handler.SetTileType((ushort)tx, (ushort)ty, TileType.Island);
        }
    }
}
