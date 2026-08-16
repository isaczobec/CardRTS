using UnityEngine;

// Shared "stamp a footprint as TileType.Island and enqueue its visual spawn" logic, used by
// every feature that places an island — IslandPlayerBaseFeature (player bases),
// MidIslandFeature (the central hub island), IslandBridgeFeature (intermittent bridge
// waypoint islands), WedgeIslandFeature (filler islands), and GemIslandFeature (gem islands) —
// so the actual placement mechanics live in exactly one place regardless of which feature
// places an island or how many of a given kind get placed.
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

        // The source prefab's own (never mutated) footprint component — still the right place
        // to read island-type-level data like HeightOffset that rotation doesn't affect.
        public readonly IslandFootprint Footprint;

        // The actual walkable shape/anchors this specific placement stamped — identical to
        // Footprint's own data when RotationDegrees is 0 (the common case), but a resampled,
        // possibly larger grid otherwise. Everything that cares where THIS island's tiles or
        // bridge anchors actually ended up (stamping, overlap checks, bridge connections,
        // resource scattering) should read through this, not Footprint directly.
        public readonly RotatedIslandFootprint RotatedFootprint;

        public readonly float RotationDegrees;

        // This placement's coarse island-graph region id (see IslandGraphBuilder) — -1 for a
        // hypothetical/candidate placement that was never actually stamped (e.g.
        // WedgeIslandFeature's own candidateIsland, built purely to evaluate a bridge
        // connection before anything is committed).
        public readonly int RegionId;

        public PlacedIsland(ushort originX, ushort originY, IslandFootprint footprint, RotatedIslandFootprint rotatedFootprint, float rotationDegrees, int regionId = -1)
        {
            OriginX = originX;
            OriginY = originY;
            Footprint = footprint;
            RotatedFootprint = rotatedFootprint;
            RotationDegrees = rotationDegrees;
            RegionId = regionId;
        }

        // Approximate world-space center of the footprint's bounding box — used by
        // IslandBridgeFeature both to pick which BridgeAnchors point faces another island
        // and to lay out a bridge's overall curve. Not tile-clamped (an island right at the
        // map edge could nominally center slightly outside it), which is fine here since
        // this is only ever used as a direction/distance reference point, never a tile
        // written to. Uses RotatedFootprint's own (possibly rotation-enlarged) bounding box,
        // not Footprint's raw one, so this stays accurate for a rotated island.
        public Vector2 CenterWorldPosition => new Vector2(OriginX + RotatedFootprint.Width / 2f, OriginY + RotatedFootprint.Height / 2f);
    }

    // Stamps prefab's IslandFootprint — rotated by rotationDegrees around the world's Y axis
    // (see FootprintRotator; 0, the default, stamps the footprint exactly as authored) — as
    // TileType.Island centered on (centerTileX, centerTileY), clamped to the world's actual
    // bounds first (see below), and enqueues an IslandSpawnAction carrying the same rotation to
    // instantiate it. Returns null (and stamps/spawns nothing) if prefab has no IslandFootprint
    // component.
    public static PlacedIsland? TryPlaceIsland(WorldGenHandler handler, GameObject prefab, int centerTileX, int centerTileY, ushort worldSize, float rotationDegrees = 0f)
    {
        IslandFootprint footprint = prefab != null ? prefab.GetComponent<IslandFootprint>() : null;
        if (footprint == null) return null;

        RotatedIslandFootprint rotated = FootprintRotator.Rotate(footprint, rotationDegrees);

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

        int originX = clampedCenterX - rotated.Width / 2;
        int originY = clampedCenterY - rotated.Height / 2;

        ushort clampedOriginX = (ushort)Mathf.Clamp(originX, 0, worldSize - 1);
        ushort clampedOriginY = (ushort)Mathf.Clamp(originY, 0, worldSize - 1);

        // Registered (and every occupied cell tagged, see StampFootprint) before this
        // island's own BridgeAnchors ever get used to build a connecting bridge, so a bridge
        // committed right after this returns (see BridgeConnectionBuilder.Commit) always
        // names an already-valid island region on that end.
        int regionId = handler.IslandGraph.BeginIsland(new Vector2(clampedOriginX + rotated.Width / 2f, clampedOriginY + rotated.Height / 2f));
        StampFootprint(handler, rotated, originX, originY, worldSize, regionId);

        Vector3 worldPosition = WorldManager.instance.TileToWorldPosition((ushort)clampedCenterX, (ushort)clampedCenterY, center: true);
        worldPosition.y += footprint.HeightOffset;
        Quaternion rotationQuat = Quaternion.Euler(0f, rotationDegrees, 0f);
        handler.EnqueueAction(new IslandSpawnAction { Prefab = prefab, WorldPosition = worldPosition, Rotation = rotationQuat });

        return new PlacedIsland(clampedOriginX, clampedOriginY, footprint, rotated, rotationDegrees, regionId);
    }

    private static void StampFootprint(WorldGenHandler handler, RotatedIslandFootprint footprint, int originX, int originY, ushort worldSize, int regionId)
    {
        foreach (Vector2Int cell in footprint.OccupiedCells())
        {
            int tx = originX + cell.x;
            int ty = originY + cell.y;
            if (tx < 0 || ty < 0 || tx >= worldSize || ty >= worldSize) continue;
            handler.SetTileType((ushort)tx, (ushort)ty, TileType.Island);
            handler.IslandGraph.MarkTile(regionId, tx, ty);
        }
    }
}
