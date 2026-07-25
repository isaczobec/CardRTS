using System;

// Reusable heal-over-time driver: every tick, for each ModifierComponent-carrying entity that
// also has a HealModifierComponent, counts TicksUntilNextProc down and, once it reaches 0,
// heals the modifier's own TargetEntityId via a HealRequest and resets the countdown to
// PeriodTicks. The modifier's own natural expiry (TicksRemaining, handled by ModifierSystem)
// is what ends the effect as a whole — this only owns the proc cadence within that lifetime.
// Mirrors DamageOverTimeSystem exactly, just healing instead of damaging.
public static class HealModifierSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<HealModifierComponent> healStore = ecs.GetComponentStore<HealModifierComponent>();
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (healStore == null || modifierStore == null) return;

        healStore.ForEach((ulong id) =>
        {
            if (!modifierStore.HasComponent(id)) return;
            if (!ModifierQuery.IsActive(ecs, id)) return;

            ref HealModifierComponent heal = ref healStore.GetComponent(id);
            heal.TicksUntilNextProc--;
            if (heal.TicksUntilNextProc > 0)
            {
                ecs.Delta.MarkComponentDirty(id, typeof(HealModifierComponent));
                return;
            }

            heal.TicksUntilNextProc = Math.Max(1, heal.PeriodTicks);
            ecs.Delta.MarkComponentDirty(id, typeof(HealModifierComponent));

            ulong targetId = modifierStore.GetComponent(id).TargetEntityId;
            ecs.Requests.CreateRequest(new HealRequest(targetId, heal.HealAdditivePerProc, heal.HealRatioPerProc));
        });
    }
}
