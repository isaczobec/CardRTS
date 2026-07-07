using System.Collections.Generic;
using UnityEngine;

public static class PathfindingSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);
    private static Dictionary<ulong, List<Vector2>> _entityIdsToPaths = new Dictionary<ulong, List<Vector2>>();

    private const float ArrivalRadius = 0.05f;

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<MoveTroopInput> inputs = ecs.GetInputsForTick<MoveTroopInput>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();

        if (inputs != null)
        {
            foreach (MoveTroopInput input in inputs)
                ApplyInput(ecs, input, posStore, movStore, troopStore);
        }

        movStore.ForEach((ulong id) => {
            if (!posStore.HasComponent(id)) return;
            if (!_entityIdsToPaths.TryGetValue(id, out List<Vector2> path)) return;

            ref PositionComponent pos = ref posStore.GetComponent(id);
            ref MovableComponent mov = ref movStore.GetComponent(id);
            Vector2 currentPos = new Vector2(pos.X, pos.Y);

            if (Vector2.Distance(path[0], currentPos) < ArrivalRadius)
            {
                path.RemoveAt(0);
                if (path.Count == 0)
                {
                    _entityIdsToPaths.Remove(id);
                    return;
                }
            }

            float step = mov.Speed * TickManager.TickInterval;
            Vector2 nextPos = Vector2.MoveTowards(currentPos, path[0], step);
            pos.X = nextPos.x;
            pos.Y = nextPos.y;
            ecs.Delta.MarkComponentDirty(id, typeof(PositionComponent));
        });
    }

    // Applies each requested (entity, destination) pair, ignoring any entity that isn't
    // movable or that the requesting client doesn't own.
    private static void ApplyInput(
        ECS ecs,
        MoveTroopInput input,
        ComponentStore<PositionComponent> posStore,
        ComponentStore<MovableComponent> movStore,
        ComponentStore<TroopComponent> troopStore)
    {
        foreach (MoveTroopInput.EntityDestination move in input.Moves)
        {
            ulong entityId = move.EntityId;
            if (!movStore.HasComponent(entityId)) continue;
            if (!posStore.HasComponent(entityId)) continue;
            if (!troopStore.HasComponent(entityId)) continue;

            ref TroopComponent troop = ref troopStore.GetComponent(entityId);
            if (troop.OwnerPlayerId != input.ClientId) continue;

            ref PositionComponent pos = ref posStore.GetComponent(entityId);
            List<Vector2> path = Pathfinding.PathFind(pos.X, pos.Y, move.DestinationX, move.DestinationY);
            if (path == null || path.Count == 0) continue;

            ref MovableComponent mov = ref movStore.GetComponent(entityId);
            mov.DestinationX = move.DestinationX;
            mov.DestinationY = move.DestinationY;
            ecs.Delta.MarkComponentDirty(entityId, typeof(MovableComponent));

            _entityIdsToPaths[entityId] = path;
        }
    }
}
