using UnityEngine;

// Places two "spawn crystal" CapturableBuildingComponent buildings on every player's own
// spoke bridge to the mid island (see IslandBridgeFeature.SpokeConnections, which already
// carries each spoke's waypoints in base-to-mid order, tagged with the owning base's
// ClientId) — the last waypoint in a spoke's own list is the "outer" one (closest to mid),
// every earlier one is "inner" (closest to that player's own base). With
// IslandBridgeFeature.SpokeIntermittentCount = 2, that's exactly one inner + one outer per
// player. Each starts OWNED by that spoke's own HomePlayerId (not neutral) — explicit design
// ask ("the spawn crystals begin as friendly to the player whose base they are closest to").
//
// Enqueue after IslandBridgeFeature (needs SpokeConnections already populated) and before
// IslandObstacleFeature (so a later obstacle patch — via SpawnedEntityRegistry, the same
// exclusion every other entity there already gets — can't land on top of one) — see
// WorldManager.SetupWorldGen.
public class CapturableBuildingFeature : WorldGenFeature
{
    public override void Generate(WorldGenHandler handler)
    {
        var bridgeFeature = handler.GetPreviousFeature<IslandBridgeFeature>();
        if (bridgeFeature == null) return;

        foreach (IslandBridgeFeature.SpokeConnection spoke in bridgeFeature.SpokeConnections)
        {
            var waypoints = spoke.OrderedWaypoints;
            for (int i = 0; i < waypoints.Count; i++)
            {
                IslandPlacementHelper.PlacedIsland island = waypoints[i];
                Vector2Int anchor = island.RotatedFootprint.BaseAnchorOrDefault();
                float x = island.OriginX + anchor.x + 0.5f;
                float y = island.OriginY + anchor.y + 0.5f;

                bool isOuter = i == waypoints.Count - 1;
                ushort homePlayerId = spoke.HomePlayerId;

                handler.EnqueueAction(new EntitySpawnAction
                {
                    X = x,
                    Y = y,
                    Spawner = (id, ecs) => EntitySpawnAction.AddCapturableBuildingComponents(id, ecs, homePlayerId, isOuter),
                });
            }
        }
    }
}
