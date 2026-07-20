using UnityEngine;

// Fires a pooled projectile from a FireProjectileOnExpireComponent modifier's target
// (ModifierComponent.TargetEntityId — whoever owns the projectile pool) in
// DirectionX/DirectionY, on the modifier's very last active tick before ModifierSystem
// deletes it. Must be registered BEFORE ModifierSystem in TickManager: this checks
// TicksRemaining <= 1, and ModifierSystem is what decrements/deletes it, so running first
// guarantees this sees exactly one tick where TicksRemaining == 1 before the entity is
// torn down, rather than racing it. Re-simulating this same tick during a client
// reconciliation replay fires "the same" projectile again deterministically (advancing the
// pool's own pointer the same way) — harmless, the same accepted pattern every other
// pooled shot in this codebase already relies on (see AbilityManager's skillshot ability).
public static class FireProjectileOnExpireSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<FireProjectileOnExpireComponent> fireStore = ecs.GetComponentStore<FireProjectileOnExpireComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (modifierStore == null || fireStore == null || posStore == null) return;

        fireStore.ForEach((ulong modifierId) =>
        {
            if (!modifierStore.HasComponent(modifierId)) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;

            ModifierComponent modifier = modifierStore.GetComponent(modifierId);
            if (modifier.TicksRemaining > 1) return;

            ulong ownerId = modifier.TargetEntityId;
            if (!posStore.HasComponent(ownerId)) return;

            FireProjectileOnExpireComponent fire = fireStore.GetComponent(modifierId);
            PositionComponent pos = posStore.GetComponent(ownerId);

            ProjectilePool.FireInDirection(ecs, ownerId, new Vector2(fire.DirectionX, fire.DirectionY), new Vector2(pos.X, pos.Y));
        });
    }
}
