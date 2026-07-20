using System;
using System.Collections.Generic;
using UnityEngine;

// Instance (not static) so each ECS gets its own path cache — a client's prediction
// ECS and the host's authoritative ECS must never share this state.
public class PathfindingSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    // The path currently being followed for an entity, plus the destination it was
    // computed for. Comparing against MovableComponent.CurrentDestinationX/Y each tick
    // lets us detect a new player order (or a future AI-driven destination change) and
    // recompute lazily, without redoing pathfinding every tick. A null Path means that
    // destination was found unreachable; we don't retry until the destination changes.
    private struct CachedPath
    {
        public List<Vector2> Path;
        public float DestinationX;
        public float DestinationY;
    }

    private readonly Dictionary<ulong, CachedPath> _entityIdsToPaths = new Dictionary<ulong, CachedPath>();

    private const float ArrivalRadius = 0.05f;
    private const int DefaultSpeed = 10;

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
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

            ref MovableComponent mov = ref movStore.GetComponent(id);
            if (mov.currentMovementMode == MovementMode.NotMoving)
            {
                _entityIdsToPaths.Remove(id);
                return;
            }

            // Authoritative "is this entity actually allowed to move right now" gate — an
            // AI system may have already set a destination/mode before a root landed
            // mid-chase; checking here (rather than only where destinations are set)
            // freezes movement immediately regardless of what set it. Leaves the cached
            // path/destination alone so it resumes exactly where it left off once able to
            // move again, rather than forgetting/re-pathing.
            if (!ActivationQuery.CanMove(ecs, id)) return;

            float destX = mov.CurrentDestinationX;
            float destY = mov.CurrentDestinationY;

            ref PositionComponent pos = ref posStore.GetComponent(id);
            Vector2 currentPos = new Vector2(pos.X, pos.Y);

            // Recompute only when there's no cached path yet, or it was computed for a
            // different destination than the one currently active.
            if (!_entityIdsToPaths.TryGetValue(id, out CachedPath cached)
                || cached.DestinationX != destX || cached.DestinationY != destY)
            {
                cached = new CachedPath
                {
                    Path         = Pathfinding.PathFind(pos.X, pos.Y, destX, destY),
                    DestinationX = destX,
                    DestinationY = destY,
                };
                _entityIdsToPaths[id] = cached;
            }

            List<Vector2> path = cached.Path;
            if (path == null || path.Count == 0) return;

            if (Vector2.Distance(path[0], currentPos) < ArrivalRadius)
            {
                path.RemoveAt(0);
                if (path.Count == 0)
                {
                    _entityIdsToPaths.Remove(id);

                    if (mov.currentMovementMode == MovementMode.MoveToPlayerSetDestination)
                        mov.playerDestinationSet = false;
                    mov.currentMovementMode = MovementMode.NotMoving;
                    ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));

                    return;
                }
            }

            int speed = StatsQuery.GetSpeed(ecs, id, DefaultSpeed);
            float step = speed * TickManager.TickInterval;
            Vector2 nextPos = Vector2.MoveTowards(currentPos, path[0], step);
            pos.X = nextPos.x;
            pos.Y = nextPos.y;
            ecs.Delta.MarkComponentDirty(id, typeof(PositionComponent));
        });
    }

    // Applies each requested (entity, destination) pair as a player move order,
    // ignoring any entity that isn't movable/owned/able to act. The actual path is
    // (re)computed lazily in Execute once the destination change is observed there.
    private void ApplyInput(
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
            if (!ActivationQuery.IsActivated(ecs, entityId)) continue;

            ref MovableComponent mov = ref movStore.GetComponent(entityId);
            mov.playerSetDestinationX = move.DestinationX;
            mov.playerSetDestinationY = move.DestinationY;
            mov.playerDestinationSet  = true;
            mov.currentMovementMode   = MovementMode.MoveToPlayerSetDestination;

            // A player move order re-homes the troop's leash point too, so it returns
            // here (rather than wherever it last leashed to) once it's done
            // chasing/fighting, regardless of which AI component (if any) it has.
            mov.LeashX = move.DestinationX;
            mov.LeashY = move.DestinationY;

            ecs.Delta.MarkComponentDirty(entityId, typeof(MovableComponent));
        }
    }
}
