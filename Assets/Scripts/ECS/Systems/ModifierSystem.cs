using System.Collections.Generic;

// Counts each ModifierComponent's TicksRemaining down by one tick — except while the
// modifier entity itself isn't active yet (e.g. still mid-deployment, if it was applied by
// playing a card; see ActivationQuery.IsActive), matching LifetimeSystem's own reasoning: a
// modifier's duration shouldn't burn away before it's actually in effect. TicksRemaining ==
// int.MaxValue means infinite — never counted down, never expires here.
//
// Once a modifier's countdown reaches 0 it's deleted outright, on the server only —
// mirrors LifetimeSystem/DeathRequest: predicted-only deletion would desync a client from
// the server's authoritative entity set, so the server's delta stream is what actually
// removes it everywhere else. Unlike LifetimeComponent, an expired modifier doesn't need to
// be flagged inactive first via IsActiveRequest/IsActivatedRequest — nothing outside a
// modifier's own entity depends on ITS activation state; whatever effect a modifier kind
// implements is expected to check ModifierQuery.IsActive itself before applying anything,
// so once TicksRemaining hits 0 there's nothing left to veto.
public static class ModifierSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static readonly List<ulong> _expired = new List<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();

        _expired.Clear();
        modifierStore.ForEach((ulong id) =>
        {
            ref ModifierComponent modifier = ref modifierStore.GetComponent(id);
            if (modifier.TicksRemaining <= 0) return;
            if (modifier.TicksRemaining == int.MaxValue) return; // infinite
            if (!ActivationQuery.IsActive(ecs, id)) return;

            modifier.TicksRemaining--;
            ecs.Delta.MarkComponentDirty(id, typeof(ModifierComponent));

            if (modifier.TicksRemaining <= 0)
                _expired.Add(id);
        });

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (ulong id in _expired)
            ecs.DeleteEntity(id);
    }
}
