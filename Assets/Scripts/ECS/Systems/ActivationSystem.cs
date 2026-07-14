// Counts each inactive entity's remaining activation delay down by one tick, and fires
// EntityActivatedEvent the tick it crosses zero. Every SpawnAtPointCard-spawned entity
// (troop, building, spell, ...) carries an ActivatableComponent for this — renderer/
// selection/deploy-progress visuals are gated on that event rather than on any specific
// gameplay component simply existing, so the same activation delay/sync applies uniformly
// regardless of what kind of entity it is.
public static class ActivationSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<ActivatableComponent> activatableStore = ecs.GetComponentStore<ActivatableComponent>();

        activatableStore.ForEach((ulong id) => {
            ref ActivatableComponent activatable = ref activatableStore.GetComponent(id);
            if (activatable._ticksUntilActive == 0) return;

            activatable._ticksUntilActive--;
            ecs.Delta.MarkComponentDirty(id, typeof(ActivatableComponent));

            if (activatable._ticksUntilActive == 0)
                flagEvents.Add(new EntityActivatedEvent { EntityId = id });
        });
    }
}
