using System;
using System.Collections.Generic;
using UnityEngine;

// Reusable "teleport the target of a modifier" effect (see TeleportingModifierComponent) —
// each tick, for every TeleportingModifierComponent whose HasTeleported is still false,
// moves ModifierComponent.TargetEntityId's PositionComponent to (DestinationX, DestinationY)
// — corrected onto the nearest cardinally-walkable tile first, if the raw destination lands
// inside terrain the nav mesh considers blocked — and sets HasTeleported true. Runs
// unconditionally (predicted on clients too — moving a PositionComponent is just a mutation,
// safe to predict, mirroring how e.g. BuyCardSystem's resource deduction is predicted), so a
// client sees its own troops blink instantly instead of waiting on the server round-trip.
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
// (e.g. MovableComponent.TeleportedTick, below) has to live on a component instead, or it
// silently goes stale/desynced the moment a reconciliation rewinds the ECS underneath it,
// which is exactly what used to cause teleported troops to visibly lag back to their old
// position and then snap forward again.
public class TeleportingModifierSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    // How far (in tiles) to search outward along each cardinal direction before giving up and
    // falling back to the raw (unwalkable) destination.
    private const int MaxSearchSteps = 32;

    private static readonly Vector2Int[] CardinalDirections =
    {
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0),
    };

    // Transient scratch, cleared+filled+drained within a single Execute() call — never read
    // across a tick boundary, so it's safe as a system-instance field (mirrors ModifierSystem's
    // own _expired list).
    private readonly List<ulong> _toDelete = new List<ulong>();

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
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
                    ResolveWalkableDestination(teleport.DestinationX, teleport.DestinationY, out float destX, out float destY);

                    ref PositionComponent pos = ref posStore.GetComponent(targetId);
                    pos.X = destX;
                    pos.Y = destY;
                    ecs.Delta.MarkComponentDirty(targetId, typeof(PositionComponent));
                }

                if (movStore != null && movStore.HasComponent(targetId))
                {
                    ref MovableComponent mov = ref movStore.GetComponent(targetId);
                    mov.TeleportedTick = ecs.CurrentSimulationTick;
                    ecs.Delta.MarkComponentDirty(targetId, typeof(MovableComponent));
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

    // If (destX, destY) is already walkable, returns it unchanged. Otherwise searches outward
    // one tile at a time along each of the four cardinal directions and returns the closest
    // walkable tile found (all four directions are checked at each step distance before
    // moving further out, so the result is genuinely the nearest cardinal match). Falls back
    // to the raw destination, unchanged, if nothing walkable is found within MaxSearchSteps.
    private static void ResolveWalkableDestination(float destX, float destY, out float resultX, out float resultY)
    {
        if (IsWalkable(destX, destY))
        {
            resultX = destX;
            resultY = destY;
            return;
        }

        for (int step = 1; step <= MaxSearchSteps; step++)
        {
            foreach (Vector2Int dir in CardinalDirections)
            {
                float candidateX = destX + dir.x * step;
                float candidateY = destY + dir.y * step;
                if (!IsWalkable(candidateX, candidateY)) continue;

                resultX = candidateX;
                resultY = candidateY;
                return;
            }
        }

        resultX = destX;
        resultY = destY;
    }

    // NavMeshHandler is only built from static terrain (see its own doc comment) — buildings
    // aren't baked in, so this only guards against out-of-bounds/blocked terrain, matching
    // what BuildingBlockingSystem.IsWalkable relies on for the same reason. Assumes walkable
    // if the nav mesh hasn't been built yet, so this never blocks a teleport before world load.
    private static bool IsWalkable(float x, float y)
        => NavMeshHandler.instance == null || NavMeshHandler.instance.GetNodeAtWorldCoords(x, y) != null;
}
