// Reads ActivatableComponent/TroopComponent state for an entity to answer "is this
// currently active" / "may this currently take actions". An entity with no
// ActivatableComponent at all (e.g. a projectile) never had a deploy delay to begin with,
// so it's always considered active.
public static class ActivationQuery
{
    public static bool IsActive(ECS ecs, ulong entityId)
    {
        ComponentStore<ActivatableComponent> store = ecs.GetComponentStore<ActivatableComponent>();
        if (store == null || !store.HasComponent(entityId)) return true;
        return store.GetComponent(entityId).IsActive;
    }

    // Single guard for "may this entity currently be interacted with / act": must have
    // finished its activation delay and, if it's a troop, not be dead. Use this instead of
    // checking IsActive alone.
    public static bool CanTakeActions(ECS ecs, ulong entityId)
    {
        if (!IsActive(ecs, entityId)) return false;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore != null && troopStore.HasComponent(entityId) && troopStore.GetComponent(entityId).IsDead)
            return false;

        return true;
    }
}
