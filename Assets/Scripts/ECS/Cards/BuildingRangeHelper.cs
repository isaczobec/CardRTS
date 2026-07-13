// Checked by SpawnAtPointCardPlaySystem (and any future card-kind play system that opts
// in via Card.RequiresFriendlyBuildingRange) against Card.MaxDistanceFromFriendlyBuilding
// before a card is allowed to play.
public static class BuildingRangeHelper
{
    // True if (x, y) is within baseRange of at least one of ownerPlayerId's own buildings,
    // after that specific building's CardPlayRangeMultiplier/CardPlayRangeBonus are applied
    // (multiplier first — see BuildingComponent).
    public static bool IsWithinRangeOfFriendlyBuilding(ECS ecs, ushort ownerPlayerId, float x, float y, float baseRange)
    {
        ComponentStore<BuildingComponent> buildingStore = ecs.GetComponentStore<BuildingComponent>();
        ComponentStore<PositionComponent> positionStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<SelectableComponent> selectableStore = ecs.GetComponentStore<SelectableComponent>();
        if (buildingStore == null || positionStore == null || selectableStore == null) return false;

        bool found = false;
        buildingStore.ForEach((ulong id) =>
        {
            if (found) return;
            if (!positionStore.HasComponent(id) || !selectableStore.HasComponent(id)) return;
            if (selectableStore.GetComponent(id).OwnerPlayerId != ownerPlayerId) return;

            BuildingComponent building = buildingStore.GetComponent(id);
            float effectiveRange = baseRange * building.CardPlayRangeMultiplier + building.CardPlayRangeBonus;
            if (effectiveRange <= 0f) return;

            PositionComponent pos = positionStore.GetComponent(id);
            float dx = pos.X - x, dy = pos.Y - y;
            if (dx * dx + dy * dy <= effectiveRange * effectiveRange)
                found = true;
        });
        return found;
    }
}
