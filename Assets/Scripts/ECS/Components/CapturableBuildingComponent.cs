// Tags a neutral/capturable objective building (see CapturableBuildingFeature/
// EntitySpawnAction.AddCapturableBuildingComponents) — placed on every ring/spoke waypoint
// island. Spawns neutral; destroying one instantly revives it owned by whoever landed the
// killing blow (see CapturableBuildingSystem), which is what actually lets a player use it as
// a friendly building (BuildingRangeHelper) to spawn troops near it. IslandRegionId is this
// building's own island's coarse region id (see IslandGraphBuilder/NavMeshHandler) — read by
// CapturableBuildingQuery to determine which OTHER capturable buildings (and which player
// base) count as "adjacent" to it for the attack-range damage reduction rules.
public struct CapturableBuildingComponent : IComponent
{
    public int IslandRegionId;

    // True for a building placed on a SPOKE waypoint island (base-to-mid-island bridge —
    // see IslandBridgeFeature.SpokeWaypointIslands/CapturableBuildingFeature), false for a
    // RING ("side", base-to-base) one. Read by CapturableBuildingQuery: owning ANY mid-bridge
    // building lets a player attack every OTHER mid-bridge building at (at least) half
    // damage, regardless of the normal one-hop island-graph adjacency requirement — explicit
    // design ask, so contesting the buildings around the shared mid island isn't gated by
    // which single spoke chain a player originally expanded up.
    public bool IsMidBridge;
}
