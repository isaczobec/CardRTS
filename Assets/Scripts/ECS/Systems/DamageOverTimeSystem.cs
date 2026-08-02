using System;

// Reusable damage-over-time driver: every tick, for each ModifierComponent-carrying entity
// that also has a DamageOverTimeComponent, counts TicksUntilNextProc down and, once it
// reaches 0, deals DamagePerProc damage to the modifier's own TargetEntityId and resets the
// countdown to PeriodTicks. The modifier's own natural expiry (TicksRemaining, handled by
// ModifierSystem) is what ends the effect as a whole — this only owns the proc cadence
// within that lifetime.
//
// Stateless (no scratch/cross-tick fields of its own — everything it needs lives in
// component data), so it's registered as a single shared GlobalSystem, same as
// ProjectileOnHitSystem/FireProjectileOnExpireSystem. Safe to run identically (and
// predicted) on both the server and every client — creating a DamageRequest here is exactly
// as harmless to duplicate as BasicMeleeAISystem's own direct DamageRequest creation.
public static class DamageOverTimeSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<DamageOverTimeComponent> dotStore = ecs.GetComponentStore<DamageOverTimeComponent>();
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (dotStore == null || modifierStore == null) return;

        dotStore.ForEach((ulong id) =>
        {
            if (!modifierStore.HasComponent(id)) return;
            if (!ModifierQuery.IsActive(ecs, id)) return;

            ref DamageOverTimeComponent dot = ref dotStore.GetComponent(id);
            dot.TicksUntilNextProc--;
            if (dot.TicksUntilNextProc > 0)
            {
                ecs.Delta.MarkComponentDirty(id, typeof(DamageOverTimeComponent));
                return;
            }

            dot.TicksUntilNextProc = Math.Max(1, dot.PeriodTicks);
            ecs.Delta.MarkComponentDirty(id, typeof(DamageOverTimeComponent));

            ulong targetId = modifierStore.GetComponent(id).TargetEntityId;
            ecs.Requests.CreateRequest(new DamageRequest(targetId, dot.DamagePerProc)
            {
                DealerEntityId = dot.DealerEntityId,
                ProcType       = DamageProcType.DamageOverTime,
            });
        });
    }
}
