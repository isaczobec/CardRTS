using System.Collections.Generic;

// Shared "which selectable entity is near this point" resolution — used by anything with
// point-and-click entity targeting (an AbilityType.TargetEntity ability today; a similarly-
// targeted card kind later). Finds the closest entity with a SelectableComponent to
// (x, y), within radius, whose ownership matches canTargetFriendly/canTargetEnemyOrNeutral
// relative to localPlayerId, and that currently passes IsSelectableRequest (the same veto
// SelectionManager itself defers to for click-selection).
public static class EntityTargeting
{
    // scratchBuffer is caller-owned (reused across calls, e.g. once per frame) rather than
    // static/shared here, so two independent callers polling in the same frame (an ability
    // preview and, later, a card preview) can never interfere with each other.
    public static ulong FindClosestSelectable(
        ECS ecs, float x, float y, float radius,
        ushort localPlayerId, bool canTargetFriendly, bool canTargetEnemyOrNeutral,
        List<ulong> scratchBuffer)
    {
        if (ecs == null || !canTargetFriendly && !canTargetEnemyOrNeutral) return 0;

        ComponentStore<SelectableComponent> selectableStore = ecs.GetComponentStore<SelectableComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (selectableStore == null || posStore == null) return 0;

        scratchBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(x, y, radius, scratchBuffer);

        ulong bestId = 0;
        float bestDistSq = radius * radius;

        foreach (ulong entityId in scratchBuffer)
        {
            if (!selectableStore.HasComponent(entityId) || !posStore.HasComponent(entityId)) continue;

            bool isFriendly = selectableStore.GetComponent(entityId).OwnerPlayerId == localPlayerId;
            if (isFriendly && !canTargetFriendly) continue;
            if (!isFriendly && !canTargetEnemyOrNeutral) continue;

            if (!IsSelectable(ecs, entityId, localPlayerId)) continue;

            PositionComponent pos = posStore.GetComponent(entityId);
            float dx = pos.X - x, dy = pos.Y - y;
            float distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestId = entityId;
            }
        }

        return bestId;
    }

    private static bool IsSelectable(ECS ecs, ulong entityId, ushort localPlayerId)
        => ecs.Requests.Process(new IsSelectableRequest(entityId, localPlayerId), ecs, executeIfNotCancelled: false).IsSelectable;
}
