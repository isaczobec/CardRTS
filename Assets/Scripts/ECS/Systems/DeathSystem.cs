using System.Collections.Generic;

// Runs at the end of the tick: any troop whose health has dropped to zero or below is
// marked dead, and — if this is the authoritative server ECS — removed from the world.
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

        if (_dead.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;

        foreach (ulong id in _dead)
        {
            ref TroopComponent troop = ref troopStore.GetComponent(id);
            troop.IsDead = true;
            ecs.Delta.MarkComponentDirty(id, typeof(TroopComponent));
            flagEvents.Add(new TroopDiedEvent { EntityId = id });

            if (isServer)
                ecs.DeleteEntity(id);
        }
    }
}
