using System;
using System.Collections.Generic;

// General "run this function later" primitive: Schedule() creates a dedicated entity
// holding a ScheduledCallComponent; every tick, Execute checks every such entity and, once
// ecs.CurrentSimulationTick reaches its Tick, invokes whichever function is registered
// under its Type (see RegisterCall) and (server-only, once every predicting client has also
// invoked its own copy — see below) deletes the entity.
//
// Runs identically on the server and every predicting client, exactly like every other
// component-driven effect in this codebase (e.g. FireProjectileOnExpireComponent) — the
// SAME deterministic cast-time code creates the SAME ScheduledCallComponent on both sides,
// and each side resolves Type to its own locally-registered function, so this needs no
// bespoke networking of its own beyond the ordinary component-create/delta/reconciliation
// pipeline already in place. A literal C# lambda can never be sent over the network — only
// the enum id + scalar params can — which is exactly what gets synced here.
//
// Instance (not static) and registered per ECS, mirroring TeleportingModifierSystem: _toDelete
// is transient scratch, cleared+filled+drained within a single Execute() call, but keeping it
// per-instance avoids any chance of one ECS's in-progress Execute() call bleeding into
// another's. The _calls registry below IS static/shared — it's just a read-only lookup table
// of pure functions, no cross-tick state.
public class ScheduledCallSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private static readonly Dictionary<ScheduledCallType, Action<ECS, ScheduledCallComponent>> _calls
        = new Dictionary<ScheduledCallType, Action<ECS, ScheduledCallComponent>>();

    // Associates a ScheduledCallType with the function it should invoke once its tick
    // arrives. Called once, at initialization time, by whichever class statically declares
    // the function — e.g. AbilityManager's static constructor for Ice Nova's own resolver.
    public static void RegisterCall(ScheduledCallType type, Action<ECS, ScheduledCallComponent> call)
        => _calls[type] = call;

    // Creates the entity backing a scheduled call, due ticksFromNow ticks from now. See
    // ScheduledCallComponent's own doc comment for what param0-3 are for.
    public static void Schedule(ECS ecs, ScheduledCallType type, int ticksFromNow, ulong param0 = 0, float param1 = 0f, float param2 = 0f, ulong param3 = 0)
    {
        EntityHandle call = ecs.CreateEntity();
        ecs.AddComponent(call.Id, new ScheduledCallComponent
        {
            Type   = type,
            Tick   = ecs.CurrentSimulationTick + (ulong)Math.Max(0, ticksFromNow),
            Param0 = param0,
            Param1 = param1,
            Param2 = param2,
            Param3 = param3,
        });
    }

    // Transient scratch, cleared+filled+drained within a single Execute() call — never read
    // across a tick boundary, so it's safe as a system-instance field (mirrors
    // TeleportingModifierSystem's own _toDelete).
    private readonly List<ulong> _toDelete = new List<ulong>();

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        ComponentStore<ScheduledCallComponent> store = ecs.GetComponentStore<ScheduledCallComponent>();
        if (store == null) return;

        _toDelete.Clear();

        store.ForEach((ulong id) =>
        {
            ref ScheduledCallComponent call = ref store.GetComponent(id);
            if (ecs.CurrentSimulationTick < call.Tick) return;

            if (!call.Invoked)
            {
                if (_calls.TryGetValue(call.Type, out Action<ECS, ScheduledCallComponent> action))
                    action(ecs, call);

                call.Invoked = true;
                ecs.Delta.MarkComponentDirty(id, typeof(ScheduledCallComponent));
            }

            _toDelete.Add(id);
        });

        // Mirrors TeleportingModifierSystem/ModifierSystem's own natural-expiry deletion —
        // predicted-only deletion would desync a client from the server's authoritative
        // entity set. The Invoked flag above (not this) is what stops the function from
        // firing again on every subsequent tick while a client waits out the one
        // round-trip until the server's deletion delta lands.
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (ulong id in _toDelete)
            ecs.DeleteEntity(id);
    }
}
