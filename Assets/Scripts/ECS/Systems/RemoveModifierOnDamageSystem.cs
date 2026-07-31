// Subscribes to DamageRequest's executed notification and ends early every active modifier
// targeting the entity that just took damage which carries a RemoveModifierOnDamageComponent
// — e.g. Sleeping Draught's silence+slow (see SleepingDraughtCard), which wakes its target
// the moment it's hit. Requires Amount > 0 after resolution — damage fully absorbed/mitigated
// down to 0 (e.g. by a Barrier) doesn't count as "taking damage" for this purpose.
//
// Sets TicksRemaining to 1 rather than deleting the modifier outright or setting it straight
// to 0 — mirrors BarrierSystem's own early-end approach: ModifierSystem's normal per-tick
// decrement handles the actual deletion (and ComponentRemovedEvent) next tick, so this
// doesn't need to duplicate that cleanup itself.
public static class RemoveModifierOnDamageSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        if (request.Amount <= 0) return;

        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore == null) return;

        ModifierQuery.ForEachActiveModifierId<RemoveModifierOnDamageComponent>(ecs, request.EntityId, modifierId =>
        {
            ref ModifierComponent modifier = ref modifierStore.GetComponent(modifierId);
            if (modifier.TicksRemaining > 1)
            {
                modifier.TicksRemaining = 1;
                ecs.Delta.MarkComponentDirty(modifierId, typeof(ModifierComponent));
            }
        });
    }
}
