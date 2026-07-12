using System.Collections.Generic;

// Runs at the end of the tick: any troop whose health has dropped to zero or below has a
// DeathRequest enqueued and then flushed. Subscribers can cancel and redirect the request
// (e.g. RespawnSystem intercepts it for respawnable entities).
public static class DeathSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static readonly List<ulong> _dead = new List<ulong>();

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
