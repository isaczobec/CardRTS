// Purely event-driven — no per-tick work of its own. In Setup, subscribes to
// RespawnableEntityRespawnedEvent (fired by RespawnSystem.Execute the moment an entity's
// cooldown reaches zero and it comes back alive) and, the first time it fires for an entity
// carrying RespawnCooldownRampComponent, overwrites RespawnableInPlaceComponent.CooldownTicks
// with CooldownTicksAfterFirstRespawn — every death after that uses the new value via
// RespawnRequest's existing "TicksUntilRespawn = CooldownTicks" logic.
//
// Safe to mutate the ECS directly from inside this FlagEvent subscriber (unlike e.g.
// BlinkSystem's old EntityActivatedEvent subscriber, which crashed doing this) because it
// only ever mutates EXISTING component fields via ref + MarkComponentDirty — it never calls
// AddComponent/DeleteEntity/FlagEvents.Add, none of which would be safe here since
// FlagEventManager.Flush() is still mid-enumeration of its own pending-events list while
// dispatching to this callback.
public static class RespawnCooldownRampSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.FlagEvents.Subscribe<RespawnableEntityRespawnedEvent>(e => OnRespawned(e, ecs));

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnRespawned(RespawnableEntityRespawnedEvent e, ECS ecs)
    {
        var rampStore = ecs.GetComponentStore<RespawnCooldownRampComponent>();
        if (rampStore == null || !rampStore.HasComponent(e.EntityId)) return;

        ref RespawnCooldownRampComponent ramp = ref rampStore.GetComponent(e.EntityId);
        if (ramp.HasRamped) return;

        var respawnStore = ecs.GetComponentStore<RespawnableInPlaceComponent>();
        if (respawnStore != null && respawnStore.HasComponent(e.EntityId))
        {
            ref RespawnableInPlaceComponent respawn = ref respawnStore.GetComponent(e.EntityId);
            respawn.CooldownTicks = ramp.CooldownTicksAfterFirstRespawn;
            ecs.Delta.MarkComponentDirty(e.EntityId, typeof(RespawnableInPlaceComponent));
        }

        ramp.HasRamped = true;
        ecs.Delta.MarkComponentDirty(e.EntityId, typeof(RespawnCooldownRampComponent));
    }
}
