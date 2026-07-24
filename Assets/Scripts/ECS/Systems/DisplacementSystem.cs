using UnityEngine;

// Drives every entity currently in a knockback/displacement (see MovableComponent's own
// doc comment on IsDisplaced). Each tick, steps PositionComponent by the entity's own stored
// DisplacementVelocityX/Y (world units/second) and counts DisplacementTicksRemaining down —
// until either it reaches 0 (displacement ends normally) or the NEXT step would land on a
// non-walkable tile, in which case displacement ends early right where the entity currently
// stands (it gets slammed into the wall instead of clipping through it).
//
// Also subscribes to CanMoveOnOwnAccountRequest/CanPerformRequest (same shape as
// ActionWindupSystem/StunnedSystem) so a displaced entity can't be steered or act while it's
// happening. Deliberately CanMoveOnOwnAccountRequest, not the broader CanMoveRequest — a
// displaced troop genuinely is still moving (just not by its own input), so CanMoveRequest
// should stay true for it; vetoing the narrower request is also what keeps PathfindingSystem
// (which now itself explicitly defers to this system for both PositionComponent and
// IsMoving while displaced — see its own IsDisplaced check) from fighting with the step
// below, without any further coordination needed between the two systems.
//
// Registered as a GlobalSystem (Execute + Setup, no per-instance scratch state) — every
// value this needs to move an entity or answer a veto check is read straight from component
// data, so (like ActionWindupSystem/StunnedSystem, and unlike e.g. PathfindingSystem's own
// per-entity path cache) there's nothing here that would go stale across a reconciliation
// replay. Position mutation runs unconditionally (predicted on clients too — moving
// PositionComponent is just a mutation, safe to predict, same reasoning as
// TeleportingModifierSystem's own doc comment); nothing here deletes an entity, so there's
// no isServer-gated step to worry about either.
public static class DisplacementSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<CanMoveOnOwnAccountRequest>((req, innerEcs) =>
        {
            if (IsDisplaced(innerEcs, req.EntityId))
                req.CanMoveOnOwnAccount = false;
        });

        ecs.Requests.Subscribe<CanPerformRequest>((req, innerEcs) =>
        {
            if (IsDisplaced(innerEcs, req.EntityId))
                req.CanPerform = false;
        });
    }

    private static bool IsDisplaced(ECS ecs, ulong entityId)
    {
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        if (movStore == null || !movStore.HasComponent(entityId)) return false;
        return movStore.GetComponent(entityId).IsDisplaced;
    }

    // Starts (or replaces) a knockback on entityId: (velocityX, velocityY) is a world-units/
    // second velocity, applied by Execute above for ticks ticks. Also clears any cached path
    // PathfindingSystem was following for this entity — it was computed for wherever the
    // entity was before the knockback, which the push is about to invalidate — so normal
    // movement recomputes a fresh path from the post-displacement position once this ends,
    // instead of resuming/continuing one anchored to a position it's no longer at. This is
    // the single entry point for starting a displacement — anything that wants to knock a
    // troop back (e.g. AbilityManager's Push ability) should call this rather than setting
    // MovableComponent's displacement fields directly, so the cache-clear can never be
    // forgotten at a new call site.
    public static void BeginDisplacement(ECS ecs, ulong entityId, float velocityX, float velocityY, int ticks)
    {
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        if (movStore == null || !movStore.HasComponent(entityId)) return;

        ref MovableComponent mov = ref movStore.GetComponent(entityId);
        mov.IsDisplaced = true;
        mov.DisplacementVelocityX = velocityX;
        mov.DisplacementVelocityY = velocityY;
        mov.DisplacementTicksRemaining = Mathf.Max(1, ticks);
        ecs.Delta.MarkComponentDirty(entityId, typeof(MovableComponent));

        ecs.GetSystem<PathfindingSystem>()?.ClearCachedPath(entityId);
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (movStore == null || posStore == null) return;

        movStore.ForEach((ulong id) =>
        {
            ref MovableComponent mov = ref movStore.GetComponent(id);
            if (!mov.IsDisplaced) return;
            if (!posStore.HasComponent(id))
            {
                EndDisplacement(ecs, id, ref mov);
                return;
            }

            ref PositionComponent pos = ref posStore.GetComponent(id);
            Vector2 currentPos = new Vector2(pos.X, pos.Y);
            Vector2 nextPos = currentPos + new Vector2(mov.DisplacementVelocityX, mov.DisplacementVelocityY) * TickManager.TickInterval;

            if (!IsWalkable(nextPos.x, nextPos.y))
            {
                // Slammed into the wall — stop exactly where it currently is rather than
                // clipping into (or partway toward) the blocked tile.
                EndDisplacement(ecs, id, ref mov);
                return;
            }

            pos.X = nextPos.x;
            pos.Y = nextPos.y;
            ecs.Delta.MarkComponentDirty(id, typeof(PositionComponent));

            mov.DisplacementTicksRemaining--;
            if (mov.DisplacementTicksRemaining <= 0)
            {
                EndDisplacement(ecs, id, ref mov);
                return;
            }

            // Mirrors PathfindingSystem's own IsMoving bookkeeping — renderers/interpolation
            // key off this, not currentMovementMode, to decide whether to smoothly lerp
            // toward the new position or snap to it (see MovableComponent.IsMoving's own
            // doc comment).
            mov.IsMoving = true;
            ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
        });
    }

    private static void EndDisplacement(ECS ecs, ulong id, ref MovableComponent mov)
    {
        mov.IsDisplaced = false;
        mov.DisplacementVelocityX = 0f;
        mov.DisplacementVelocityY = 0f;
        mov.DisplacementTicksRemaining = 0;
        mov.IsMoving = false;
        ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
    }

    // Mirrors TeleportingModifierSystem's own private walkability check (no shared helper
    // exists yet in this codebase — BuildingBlockingSystem has its own slightly different
    // copy that NREs instead of treating a null NavMeshHandler.instance as walkable).
    private static bool IsWalkable(float x, float y)
        => NavMeshHandler.instance == null || NavMeshHandler.instance.GetNodeAtWorldCoords(x, y) != null;
}
