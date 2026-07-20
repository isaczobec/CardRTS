// Counts each inactive entity's remaining activation delay down by one tick, and fires
// EntityActivatedEvent the tick it crosses zero. Every SpawnAtPointCard-spawned entity
// (troop, building, spell, ...) carries an ActivatableComponent for this — renderer/
// selection/deploy-progress visuals are gated on that event rather than on any specific
// gameplay component simply existing, so the same activation delay/sync applies uniformly
// regardless of what kind of entity it is.
public static class ActivationSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    // Owns the ActivatableComponent side of IsActiveRequest/IsActivatedRequest — other
    // orthogonal veto reasons (TroopComponent.IsDead, an expired LifetimeComponent, ...)
    // subscribe independently wherever they live (see DeathSystem, LifetimeSystem) rather
    // than here.
    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<IsActiveRequest>((req, innerEcs) =>
        {
            if (!IsActiveByComponent(innerEcs, req.EntityId))
                req.IsActive = false;
        });

        ecs.Requests.Subscribe<IsActivatedRequest>((req, innerEcs) =>
        {
            if (!IsActiveByComponent(innerEcs, req.EntityId))
                req.IsActivated = false;
        });
    }

    // An entity with no ActivatableComponent at all (e.g. a projectile) never had a
    // deploy delay to begin with, so it's always considered active.
    private static bool IsActiveByComponent(ECS ecs, ulong entityId)
    {
        ComponentStore<ActivatableComponent> store = ecs.GetComponentStore<ActivatableComponent>();
        if (store == null || !store.HasComponent(entityId)) return true;
        return store.GetComponent(entityId).IsActive;
    }

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
