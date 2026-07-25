using System;

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

    // Finds the (at most one) active modifier entity targeting targetEntityId that also
    // carries a T payload component — generalizes the "ForEach the payload store, filter by
    // ModifierComponent.TargetEntityId + IsActive" lookup duplicated across several systems
    // into one shared implementation. Returns 0 if none found. Use this only where at most one
    // such modifier is ever expected to target the same entity at once — see
    // ForEachActiveModifierId for the stacking case (e.g. buying the same upgrade twice).
    public static ulong FindActiveModifierId<T>(ECS ecs, ulong targetEntityId) where T : struct, IComponent
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<T> payloadStore = ecs.GetComponentStore<T>();
        if (modifierStore == null || payloadStore == null) return 0;

        ulong found = 0;
        payloadStore.ForEach((ulong modifierId) =>
        {
            if (found != 0) return;
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != targetEntityId) return;
            if (!IsActive(ecs, modifierId)) return;
            found = modifierId;
        });
        return found;
    }

    // Invokes action once for every active modifier entity targeting targetEntityId that also
    // carries a T payload component — the "stacking" counterpart to FindActiveModifierId, for
    // effects where more than one instance targeting the same entity should each act
    // independently (e.g. GiantsbaneSystem/FocusFireSystem/LifestealSystem, where buying the
    // same upgrade more than once creates a separate modifier entity per purchase — see
    // CardUpgrade/UpgradeQuery — and each should proc/heal/stack on its own).
    public static void ForEachActiveModifierId<T>(ECS ecs, ulong targetEntityId, Action<ulong> action) where T : struct, IComponent
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<T> payloadStore = ecs.GetComponentStore<T>();
        if (modifierStore == null || payloadStore == null) return;

        payloadStore.ForEach((ulong modifierId) =>
        {
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != targetEntityId) return;
            if (!IsActive(ecs, modifierId)) return;
            action(modifierId);
        });
    }
}
