using UnityEngine;

// Instantly repositions EntityId to (DestinationX, DestinationY) — corrected onto the
// nearest cardinally-walkable tile first, if the raw destination lands inside terrain the
// nav mesh considers blocked — and stamps MovableComponent.TeleportedTick so the position
// interpolator snaps instead of lerping across the (potentially huge) instantaneous jump.
// Factored out of TeleportingModifierSystem's own Execute, which now just calls
// ecs.Requests.Process(new TeleportRequest(...), ecs) once per tick for each modifier whose
// teleport hasn't happened yet, instead of inlining this logic itself.
//
// Deliberately checked by neither CanMoveRequest nor CanMoveOnOwnAccountRequest — a
// teleport is an instant, absolute reposition that ignores normal movement rules entirely
// (see CanMoveRequest's own doc comment: "except teleportation"), so nothing here queries
// either; this always succeeds as long as the entity has a PositionComponent.
public class TeleportRequest : Request
{
    // How far (in tiles) to search outward along each cardinal direction before giving up
    // and falling back to the raw (unwalkable) destination.
    private const int MaxSearchSteps = 32;

    private static readonly Vector2Int[] CardinalDirections =
    {
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0),
    };

    public readonly ulong EntityId;
    public readonly float DestinationX;
    public readonly float DestinationY;

    public TeleportRequest(ulong entityId, float destinationX, float destinationY)
    {
        EntityId = entityId;
        DestinationX = destinationX;
        DestinationY = destinationY;
    }

    public override void Execute(ECS ecs)
    {
        ResolveWalkableDestination(DestinationX, DestinationY, out float destX, out float destY);

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore != null && posStore.HasComponent(EntityId))
        {
            ref PositionComponent pos = ref posStore.GetComponent(EntityId);
            pos.X = destX;
            pos.Y = destY;
            ecs.Delta.MarkComponentDirty(EntityId, typeof(PositionComponent));
        }

        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        if (movStore != null && movStore.HasComponent(EntityId))
        {
            ref MovableComponent mov = ref movStore.GetComponent(EntityId);
            mov.TeleportedTick = ecs.CurrentSimulationTick;
            ecs.Delta.MarkComponentDirty(EntityId, typeof(MovableComponent));
        }

        // Whatever path PathfindingSystem was following for this entity was computed for
        // (and anchored to) wherever it stood before this jump — stale the instant the
        // teleport happens, so normal movement needs a fresh Pathfinding.PathFind from the
        // new position rather than resuming/continuing one anchored to the old one.
        ecs.GetSystem<PathfindingSystem>()?.ClearCachedPath(EntityId);
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
