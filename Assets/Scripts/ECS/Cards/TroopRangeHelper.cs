// Checked by SpawnAtPointCardPlaySystem for cards with Card.AllowsFriendlyTroopRange()
// enabled, against Card.MaxDistanceFromFriendlyTroop — in addition to (not instead of)
// BuildingRangeHelper's friendly-building check.
public static class TroopRangeHelper
{
    // True if (x, y) is within baseRange of at least one of ownerPlayerId's own physical
    // troops (TroopComponent.IsPhysicalTroop) — buildings, neutral resource nodes, and
    // similar non-troop entities that only carry TroopComponent for ownership never count.
    // Unlike BuildingRangeHelper, there's no per-entity multiplier/bonus: baseRange applies
    // directly to every troop.
    public static bool IsWithinRangeOfFriendlyTroop(ECS ecs, ushort ownerPlayerId, float x, float y, float baseRange)
    {
        if (baseRange <= 0f) return false;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<PositionComponent> positionStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<SelectableComponent> selectableStore = ecs.GetComponentStore<SelectableComponent>();
        if (troopStore == null || positionStore == null || selectableStore == null) return false;

        bool found = false;
        float range2 = baseRange * baseRange;
        troopStore.ForEach((ulong id) =>
        {
            if (found) return;
            if (!troopStore.GetComponent(id).IsPhysicalTroop) return;
            if (!positionStore.HasComponent(id) || !selectableStore.HasComponent(id)) return;
            if (selectableStore.GetComponent(id).OwnerPlayerId != ownerPlayerId) return;

            PositionComponent pos = positionStore.GetComponent(id);
            float dx = pos.X - x, dy = pos.Y - y;
            if (dx * dx + dy * dy <= range2)
                found = true;
        });
        return found;
    }
}
