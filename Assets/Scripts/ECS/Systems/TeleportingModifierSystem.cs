using System.Collections.Generic;

// Reusable "teleport the target of a modifier" effect (see TeleportingModifierComponent) —
// each tick, for every TeleportingModifierComponent whose HasTeleported is still false,
// moves ModifierComponent.TargetEntityId's PositionComponent to (DestinationX, DestinationY)
// and sets HasTeleported true. Runs unconditionally (predicted on clients too — moving a
// PositionComponent is just a mutation, safe to predict, mirroring how e.g. BuyCardSystem's
// resource deduction is predicted), so a client sees its own troops blink instantly instead
// of waiting on the server round-trip.
//
// Once a modifier has done its job (HasTeleported is true, whether just set this tick or
// already set from a previous one — e.g. while a client waits on the server's confirmation),
// the modifier entity is deleted — but, like ModifierSystem's own expiry, only on the
// server; a client just leaves the (inert, already-consumed) entity in place until the
// server's delta removes it everywhere.
public static class TeleportingModifierSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static readonly List<ulong> _toDelete = new List<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<TeleportingModifierComponent> teleportStore = ecs.GetComponentStore<TeleportingModifierComponent>();
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        _toDelete.Clear();

        teleportStore.ForEach((ulong id) =>
        {
            if (!modifierStore.HasComponent(id)) return;
            if (!ActivationQuery.IsActive(ecs, id)) return;

            ref TeleportingModifierComponent teleport = ref teleportStore.GetComponent(id);

            if (!teleport.HasTeleported)
            {
                ulong targetId = modifierStore.GetComponent(id).TargetEntityId;
                if (posStore != null && posStore.HasComponent(targetId))
                {
                    ref PositionComponent pos = ref posStore.GetComponent(targetId);
                    pos.X = teleport.DestinationX;
                    pos.Y = teleport.DestinationY;
                    ecs.Delta.MarkComponentDirty(targetId, typeof(PositionComponent));
                }

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
