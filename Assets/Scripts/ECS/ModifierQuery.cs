// Reads ModifierComponent state for a modifier entity to answer "is this buff/debuff
// currently in effect". A modifier can itself be an activatable entity — e.g. one applied
// by playing a card carries the same ActivatableComponent deployment delay every other
// SpawnAtPointCard-spawned entity does — so this defers to ActivationQuery.IsActive, which
// already treats "no ActivatableComponent at all" as always-active, meaning a modifier
// spawned instantly (e.g. on a projectile hit, no deploy delay) needs no special-casing
// here.
public static class ModifierQuery
{
    public static bool IsActive(ECS ecs, ulong modifierEntityId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore == null || !modifierStore.HasComponent(modifierEntityId)) return false;

        if (modifierStore.GetComponent(modifierEntityId).TicksRemaining <= 0) return false;

        return ActivationQuery.IsActive(ecs, modifierEntityId);
    }
}
