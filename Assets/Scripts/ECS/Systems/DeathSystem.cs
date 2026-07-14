using System.Collections.Generic;

// Runs at the end of the tick: any troop whose health has dropped to zero or below has a
// DeathRequest enqueued and then flushed. Subscribers can cancel and redirect the request
// (e.g. RespawnSystem intercepts it for respawnable entities).
public static class DeathSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static readonly List<ulong> _dead = new List<ulong>();

    // Owns the "dead troops can't act" veto for CanTakeActionsRequest — orthogonal to
    // ActivationSystem's ActivatableComponent veto and LifetimeSystem's expiry veto, each
    // subscribing independently for its own reason.
    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<CanTakeActionsRequest>((req, innerEcs) =>
        {
            var troopStore = innerEcs.GetComponentStore<TroopComponent>();
            if (troopStore != null && troopStore.HasComponent(req.EntityId) && troopStore.GetComponent(req.EntityId).IsDead)
                req.CanTakeActions = false;
        });
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();

        _dead.Clear();
        healthStore.ForEach((ulong id) => {
            if (!troopStore.HasComponent(id)) return;
            if (troopStore.GetComponent(id).IsDead) return;
            if (healthStore.GetComponent(id).CurrentHealth > 0) return;
            _dead.Add(id);
        });

        foreach (ulong id in _dead)
            ecs.Requests.CreateRequest(new DeathRequest(id));

        ecs.Requests.Flush<DeathRequest>(ecs);
    }
}
