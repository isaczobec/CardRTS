using System.Collections.Generic;
using UnityEngine;

public static class PathfindingSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);
    private static Dictionary<ulong, List<Vector2>> _entityIdsToPaths = new Dictionary<ulong, List<Vector2>>();

    private const float Speed = 10f;
    private const float ArrivalRadius = 0.05f;

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<MoveTroopInput> inputs = ecs.GetInputsForTick<MoveTroopInput>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        if (inputs != null && inputs.Count > 0)
        {
            MoveTroopInput input = inputs[0];
            posStore.ForEach((ulong id) => {
                ref PositionComponent pos = ref posStore.GetComponent(id);
                List<Vector2> path = Pathfinding.PathFind(pos.X, pos.Y, input.X, input.Y);
                if (path == null || path.Count == 0) return;
                _entityIdsToPaths[id] = path;
            });
        }

        float step = Speed * TickManager.TickInterval;

        posStore.ForEach((ulong id) => {
            if (!_entityIdsToPaths.ContainsKey(id)) return;
            List<Vector2> path = _entityIdsToPaths[id];
            ref PositionComponent pos = ref posStore.GetComponent(id);
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

            Vector2 nextPos = Vector2.MoveTowards(currentPos, path[0], step);
            pos.X = nextPos.x;
            pos.Y = nextPos.y;
            ecs.Delta.MarkComponentDirty(id, typeof(PositionComponent));
        });
    }
}
