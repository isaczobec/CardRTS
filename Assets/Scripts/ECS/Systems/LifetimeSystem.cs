using System.Collections.Generic;

// Counts each LifetimeComponent entity's TicksRemaining down by one tick. Once it reaches
// zero the entity is flagged inactive/unable to act — via Setup's IsActiveRequest/
// IsActivatedRequest subscriptions, the same veto mechanism ActivationSystem uses for
// ActivatableComponent and DeathSystem uses for TroopComponent.IsDead — and, on the server
// only, deleted outright. Mirrors DeathRequest, which likewise only ever calls
// ecs.DeleteEntity when isServer: predicted-only deletion would desync a client from the
// server's authoritative entity set, so the server's delta stream is what actually removes
// it everywhere else.
public static class LifetimeSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static readonly List<ulong> _expired = new List<ulong>();

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<IsActiveRequest>((req, innerEcs) =>
        {
            if (IsExpired(innerEcs, req.EntityId))
                req.IsActive = false;
        });

        ecs.Requests.Subscribe<IsActivatedRequest>((req, innerEcs) =>
        {
            if (IsExpired(innerEcs, req.EntityId))
                req.IsActivated = false;
        });
    }

    private static bool IsExpired(ECS ecs, ulong entityId)
    {
        ComponentStore<LifetimeComponent> store = ecs.GetComponentStore<LifetimeComponent>();
        return store != null && store.HasComponent(entityId) && store.GetComponent(entityId).TicksRemaining <= 0;
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<LifetimeComponent> lifetimeStore = ecs.GetComponentStore<LifetimeComponent>();

        _expired.Clear();
        lifetimeStore.ForEach((ulong id) =>
        {
            ref LifetimeComponent lifetime = ref lifetimeStore.GetComponent(id);
            if (lifetime.TicksRemaining <= 0) return;
            // Don't burn duration while still deploying (or otherwise inactive for some
            // other reason) — the countdown only starts once the entity is actually live.
            if (!ActivationQuery.IsActive(ecs, id)) return;

            lifetime.TicksRemaining--;
            ecs.Delta.MarkComponentDirty(id, typeof(LifetimeComponent));

            if (lifetime.TicksRemaining <= 0)
                _expired.Add(id);
        });

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (ulong id in _expired)
            ecs.DeleteEntity(id);
    }
}
