using System.Collections.Generic;
using UnityEngine;

// Places one neutral CapturableBuildingComponent building at the center of every intermittent
// waypoint island threaded along the main bridge network — see IslandBridgeFeature.
// WaypointIslands, which already covers both the RING ("side", base-to-base) and SPOKE ("mid",
// base-to-mid-island) waypoints in one list, exactly what "each intermittent island on the side
// and the mid bridges" means. Neutral on first spawn; see CapturableBuildingSystem for the
// capture-on-destroy/attack-range damage reduction mechanics that turn these into the map's
// "expand from your base" objectives.
//
// Enqueue after IslandBridgeFeature (needs WaypointIslands already populated) and before
// IslandObstacleFeature (so a later obstacle patch — via SpawnedEntityRegistry, the same
// exclusion every other entity there already gets — can't land on top of one) — see
// WorldManager.SetupWorldGen.
public class CapturableBuildingFeature : WorldGenFeature
{
    public override void Generate(WorldGenHandler handler)
    {
        var bridgeFeature = handler.GetPreviousFeature<IslandBridgeFeature>();
        if (bridgeFeature == null) return;

        // Region ids of every SPOKE (base-to-mid-island) waypoint island — the "mid bridge"
        // half of WaypointIslands — so the building placed on each one below can be tagged
        // IsMidBridge accordingly. See CapturableBuildingComponent.IsMidBridge's own comment.
        var spokeRegionIds = new HashSet<int>();
        foreach (IslandPlacementHelper.PlacedIsland spokeIsland in bridgeFeature.SpokeWaypointIslands)
            spokeRegionIds.Add(spokeIsland.RegionId);

        foreach (IslandPlacementHelper.PlacedIsland island in bridgeFeature.WaypointIslands)
        {
            Vector2Int anchor = island.RotatedFootprint.BaseAnchorOrDefault();
            float x = island.OriginX + anchor.x + 0.5f;
            float y = island.OriginY + anchor.y + 0.5f;

            int regionId = island.RegionId;
            bool isMidBridge = spokeRegionIds.Contains(regionId);
            handler.EnqueueAction(new EntitySpawnAction
            {
                X = x,
                Y = y,
                Spawner = (id, ecs) => EntitySpawnAction.AddCapturableBuildingComponents(id, ecs, regionId, isMidBridge),
            });
        }
    }
}
