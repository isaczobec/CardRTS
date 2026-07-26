// In Setup:
//  - Pre-execute, subscribes to DeathRequest for respawnable entities and sets
//    ShouldDelete = false, so DeathRequest.Execute still runs its normal IsDead/
//    TroopDiedEvent/"executed"-notification logic, but leaves the entity alive to be
//    revived in place instead of deleting it.
//  - Once that death has actually executed, processes a RespawnRequest immediately (see
//    RespawnRequest.Execute for the actual "start the cooldown countdown and fire
//    RespawnableEntityDiedEvent" logic).
// In Execute, counts down the cooldown each tick. On expiry, clears IsDead, restores
// health to MaxHealth, and fires RespawnableEntityRespawnedEvent — see
// RespawnCooldownRampSystem for one thing that reacts to that event.
public static class RespawnSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<IsSelectableRequest>((req, innerEcs) =>
        {
            var respawnStore = innerEcs.GetComponentStore<RespawnableInPlaceComponent>();
            if (respawnStore == null || !respawnStore.HasComponent(req.EntityId)) return;

            var troopStore = innerEcs.GetComponentStore<TroopComponent>();
            if (troopStore != null && troopStore.HasComponent(req.EntityId) && troopStore.GetComponent(req.EntityId).IsDead)
                req.IsSelectable = false;
        });

        ecs.Requests.Subscribe<DeathRequest>((req, innerEcs) =>
        {
            var respawnStore = innerEcs.GetComponentStore<RespawnableInPlaceComponent>();
            if (respawnStore == null || !respawnStore.HasComponent(req.EntityId)) return;

            req.ShouldDelete = false;
        });

        ecs.Requests.SubscribeExecuted<DeathRequest>((req, innerEcs) =>
        {
            innerEcs.Requests.Process(new RespawnRequest(req.EntityId), innerEcs);
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

            PositionQuery.TryGet(ecs, id, out float x, out float y);
            flagEvents.Add(new RespawnableEntityRespawnedEvent { EntityId = id, X = x, Y = y });
        });
    }
}
