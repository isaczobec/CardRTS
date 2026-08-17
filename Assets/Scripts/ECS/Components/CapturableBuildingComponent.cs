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
}
