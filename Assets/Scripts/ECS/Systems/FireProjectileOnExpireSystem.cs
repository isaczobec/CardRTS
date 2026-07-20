using UnityEngine;

// Fires a pooled projectile in DirectionX/DirectionY from a FireProjectileOnExpireComponent
// modifier, on the modifier's very last active tick before ModifierSystem deletes it. The
// shot's origin is always ModifierComponent.TargetEntityId's position (whoever's winding
// up), but the POOL it's drawn from is FireProjectileOnExpireComponent.ProjectilePoolOwnerId
// (falling back to TargetEntityId's own pool if that's 0) — see that field's own doc
// comment for why the two can differ. Must be registered BEFORE ModifierSystem in
// TickManager: this checks TicksRemaining <= 1, and ModifierSystem is what
// decrements/deletes it, so running first guarantees this sees exactly one tick where
// TicksRemaining == 1 before the entity is torn down, rather than racing it. Re-simulating
// this same tick during a client reconciliation replay fires "the same" projectile again
// deterministically (advancing the pool's own pointer the same way) — harmless, the same
// accepted pattern every other pooled shot in this codebase already relies on (see
// AbilityManager's skillshot ability).
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

            ulong casterId = modifier.TargetEntityId;
            if (!posStore.HasComponent(casterId)) return;

            FireProjectileOnExpireComponent fire = fireStore.GetComponent(modifierId);
            ulong poolOwnerId = fire.ProjectilePoolOwnerId != 0 ? fire.ProjectilePoolOwnerId : casterId;

            PositionComponent pos = posStore.GetComponent(casterId);
            ProjectilePool.FireInDirection(ecs, poolOwnerId, new Vector2(fire.DirectionX, fire.DirectionY), new Vector2(pos.X, pos.Y));
        });
    }
}
