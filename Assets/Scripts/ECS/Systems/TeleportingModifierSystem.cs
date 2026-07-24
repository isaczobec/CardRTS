using System;
using System.Collections.Generic;

// Reusable "teleport the target of a modifier" effect (see TeleportingModifierComponent) —
// each tick, for every TeleportingModifierComponent whose HasTeleported is still false,
// processes a TeleportRequest (see that class for the actual walkable-destination
// resolution/PositionComponent write/TeleportedTick stamp/cached-path clear) targeting
// ModifierComponent.TargetEntityId, then sets HasTeleported true. Runs unconditionally
// (predicted on clients too — moving a PositionComponent is just a mutation, safe to
// predict, mirroring how e.g. BuyCardSystem's resource deduction is predicted), so a client
// sees its own troops blink instantly instead of waiting on the server round-trip.
//
// Once a modifier has done its job (HasTeleported is true, whether just set this tick or
// already set from a previous one — e.g. while a client waits on the server's confirmation),
// the modifier entity is deleted — but, like ModifierSystem's own expiry, only on the
// server; a client just leaves the (inert, already-consumed) entity in place until the
// server's delta removes it everywhere.
//
// Instance (not static) and registered per ECS, like TargetingSystem/BlinkSystem, so a
// client's prediction ECS and the host's authoritative ECS keep independent _toDelete
// scratch lists. Deliberately does NOT keep any other cross-tick state as a system-instance
// field: RunReconciliation's ClientLocalECS.CopyStateFrom(ClientServerMirrorECS) replaces
// this ECS's component-store data wholesale on every reconciliation, but has no way to
// touch an ordinary C# field living on a system instance — any state needed across ticks
// (e.g. MovableComponent.TeleportedTick, stamped by TeleportRequest) has to live on a
// component instead, or it silently goes stale/desynced the moment a reconciliation rewinds
// the ECS underneath it, which is exactly what used to cause teleported troops to visibly
// lag back to their old position and then snap forward again.
public class TeleportingModifierSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    // Transient scratch, cleared+filled+drained within a single Execute() call — never read
    // across a tick boundary, so it's safe as a system-instance field (mirrors ModifierSystem's
    // own _expired list).
    private readonly List<ulong> _toDelete = new List<ulong>();

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        ComponentStore<TeleportingModifierComponent> teleportStore = ecs.GetComponentStore<TeleportingModifierComponent>();
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();

        _toDelete.Clear();

        teleportStore.ForEach((ulong id) =>
        {
            if (!modifierStore.HasComponent(id)) return;
            if (!ActivationQuery.IsActive(ecs, id)) return;

            ref TeleportingModifierComponent teleport = ref teleportStore.GetComponent(id);

            if (!teleport.HasTeleported)
            {
                ulong targetId = modifierStore.GetComponent(id).TargetEntityId;
                ecs.Requests.Process(new TeleportRequest(targetId, teleport.DestinationX, teleport.DestinationY), ecs);

                teleport.HasTeleported = true;
                ecs.Delta.MarkComponentDirty(id, typeof(TeleportingModifierComponent));
            }

            _toDelete.Add(id);
        });

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (ulong id in _toDelete)
            ecs.DeleteEntity(id);
    }
}
