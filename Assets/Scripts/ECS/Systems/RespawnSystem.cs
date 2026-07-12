// In Setup, subscribes to DeathRequest to intercept death for respawnable entities:
// cancels normal death, marks IsDead so the entity is skipped next tick, starts the
// cooldown countdown, and fires RespawnableEntityDiedEvent.
// In Execute, counts down the cooldown each tick. On expiry, clears IsDead, restores
// health to MaxHealth, and fires RespawnableEntityRespawnedEvent.
public static class RespawnSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<DeathRequest>((req, innerEcs) =>
        {
            var respawnStore = innerEcs.GetComponentStore<RespawnableInPlaceComponent>();
            if (respawnStore == null || !respawnStore.HasComponent(req.EntityId)) return;

            req.Cancel();

            var troopStore = innerEcs.GetComponentStore<TroopComponent>();
            if (troopStore != null && troopStore.HasComponent(req.EntityId))
            {
                ref TroopComponent troop = ref troopStore.GetComponent(req.EntityId);
                troop.IsDead = true;
                innerEcs.Delta.MarkComponentDirty(req.EntityId, typeof(TroopComponent));
            }

            ref RespawnableInPlaceComponent respawn = ref respawnStore.GetComponent(req.EntityId);
            respawn.TicksUntilRespawn = respawn.CooldownTicks;
            innerEcs.Delta.MarkComponentDirty(req.EntityId, typeof(RespawnableInPlaceComponent));

            innerEcs.FlagEvents.Add(new RespawnableEntityDiedEvent { EntityId = req.EntityId });
        });
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<RespawnableInPlaceComponent> respawnStore = ecs.GetComponentStore<RespawnableInPlaceComponent>();
        if (respawnStore == null) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        ComponentStore<StatsComponent> statsStore = ecs.GetComponentStore<StatsComponent>();

        respawnStore.ForEach((ulong id) =>
        {
            ref RespawnableInPlaceComponent respawn = ref respawnStore.GetComponent(id);
            if (!respawn.IsOnCooldown) return;

            respawn.TicksUntilRespawn--;
            ecs.Delta.MarkComponentDirty(id, typeof(RespawnableInPlaceComponent));

            if (respawn.IsOnCooldown) return;

            if (troopStore != null && troopStore.HasComponent(id))
            {
                ref TroopComponent troop = ref troopStore.GetComponent(id);
                troop.IsDead = false;
                ecs.Delta.MarkComponentDirty(id, typeof(TroopComponent));
            }

            if (healthStore != null && healthStore.HasComponent(id) &&
                statsStore != null && statsStore.HasComponent(id))
            {
                ref HealthComponent health = ref healthStore.GetComponent(id);
                health.CurrentHealth = statsStore.GetComponent(id).MaxHealth;
                ecs.Delta.MarkComponentDirty(id, typeof(HealthComponent));
            }

            flagEvents.Add(new RespawnableEntityRespawnedEvent { EntityId = id });
        });
    }
}
