using UnityEngine;

// Shared "world position, tick-interpolated for smooth rendering" resolution for any entity
// with a PositionComponent — every renderer that needs to visually follow a moving entity
// (HealthBarManager, ModifierIconManager, SelectionManager, CardRangeIndicatorManager, ...)
// used to hand-roll this same sequence itself: read PositionComponent, convert to world
// space via WorldManager's own terrain-height lookup, read MovableComponent.IsMoving/
// TeleportedTick (defaulting to "not moving" when there's no MovableComponent at all, e.g. a
// stationary building), then feed all of that into a TickPositionInterpolator.Update call.
// This just centralizes that sequence so a new renderer (see PrefabModifierRenderer) can
// reuse it instead of duplicating it again.
public static class EntityPositionQuery
{
    // False (leaving worldPos at default) if entityId has no PositionComponent at all — e.g.
    // it was deleted since the caller last saw it, or hasn't been created yet. heightOffset
    // is added on top of the terrain height at entityId's own tile, matching every existing
    // WorldPositionFor helper's own +offset convention (e.g. a status-effect prefab floating
    // above a troop's head). interpolator is caller-owned (mirrors every existing renderer's
    // own per-instance TickPositionInterpolator field) rather than shared/static here, so
    // independent renderers interpolating the same entity never interfere with each other.
    public static bool TryGetInterpolatedPosition(
        ECS ecs, TickPositionInterpolator interpolator, ulong entityId, float heightOffset, out Vector3 worldPos)
    {
        worldPos = default;
        if (ecs == null || interpolator == null) return false;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return false;

        PositionComponent pos = posStore.GetComponent(entityId);
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        Vector3 rawWorldPos = new Vector3(pos.X, height + heightOffset, pos.Y);

        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        bool isMoving = movStore != null && movStore.HasComponent(entityId) && movStore.GetComponent(entityId).IsMoving;
        bool teleported = movStore != null && movStore.HasComponent(entityId)
            && movStore.GetComponent(entityId).TeleportedTick == ecs.CurrentSimulationTick;

        worldPos = interpolator.Update(entityId, rawWorldPos, isMoving, teleported);
        return true;
    }
}
