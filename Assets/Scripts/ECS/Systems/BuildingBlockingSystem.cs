using System;
using System.Collections.Generic;
using UnityEngine;

// Keeps idle troops from standing inside a building's footprint. Each tick, for every
// BuildingComponent entity, any troop (PositionComponent + MovableComponent) within
// BlockRadius that isn't currently moving is instantly relocated to the nearest walkable
// point just outside the radius. A troop that IS moving is left alone — it's already
// headed somewhere and PathfindingSystem/the nav mesh will route it around the building's
// own collision on its own; this system only handles the troops that would otherwise just
// stand there overlapping it (e.g. a building dropped on top of someone, or an order
// finishing while a troop happens to be inside the radius).
// Instance (not static) and registered per ECS, like PathfindingSystem, so a client's
// prediction ECS and the host's authoritative ECS each run their own copy.
public class BuildingBlockingSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private const float PushMargin = 0.5f;
    private const float RadiusStep = 1f;
    private const int MaxRadiusSteps = 8;
    private const int AngleSamples = 16;

    private readonly List<ulong> _queryBuffer = new List<ulong>();

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        ComponentStore<BuildingComponent> buildingStore = ecs.GetComponentStore<BuildingComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();

        buildingStore.ForEach((ulong buildingId) => Tick(ecs, buildingId, buildingStore, posStore, movStore));
    }

    private void Tick(ECS ecs, ulong buildingId,
        ComponentStore<BuildingComponent> buildingStore,
        ComponentStore<PositionComponent> posStore,
        ComponentStore<MovableComponent> movStore)
    {
        if (!posStore.HasComponent(buildingId)) return;

        BuildingComponent building = buildingStore.GetComponent(buildingId);
        PositionComponent buildingPos = posStore.GetComponent(buildingId);

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(buildingPos.X, buildingPos.Y, building.BlockRadius, _queryBuffer);

        foreach (ulong troopId in _queryBuffer)
        {
            if (troopId == buildingId) continue;
            if (!posStore.HasComponent(troopId) || !movStore.HasComponent(troopId)) continue;

            ref MovableComponent mov = ref movStore.GetComponent(troopId);
            if (mov.currentMovementMode != MovementMode.NotMoving) continue;

            ref PositionComponent troopPos = ref posStore.GetComponent(troopId);

            if (!TryFindWalkablePointOutside(buildingPos.X, buildingPos.Y, building.BlockRadius,
                    troopPos.X, troopPos.Y, out float newX, out float newY))
                continue;

            troopPos.X = newX;
            troopPos.Y = newY;
            ecs.Delta.MarkComponentDirty(troopId, typeof(PositionComponent));
            ecs.FlagEvents.Add(new PositionUpdatedEvent());

            // If the troop's "go home when idle" leash point is itself inside the
            // building's radius, re-home it to the spot we just relocated it to —
            // otherwise GoHome would walk it right back in the moment it has no target,
            // and we'd push it out again next tick, forever. Leash lives on
            // MovableComponent (shared by every movable troop), so this needs no
            // per-AI-type code path.
            if (IsInsideRadius(mov.LeashX, mov.LeashY, buildingPos, building.BlockRadius))
            {
                mov.LeashX = newX;
                mov.LeashY = newY;
                ecs.Delta.MarkComponentDirty(troopId, typeof(MovableComponent));
            }
        }
    }

    private static bool IsInsideRadius(float x, float y, PositionComponent center, float radius)
    {
        float dx = x - center.X, dy = y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    // Searches outward ring by ring (starting just past blockRadius) for a walkable tile,
    // sampling angles alternating left/right of the direction from the building to the
    // troop's current spot first — so the troop ends up as close as possible to where it
    // already was, rather than always drifting the same way around the building.
    private static bool TryFindWalkablePointOutside(
        float centerX, float centerY, float blockRadius,
        float preferredX, float preferredY,
        out float resultX, out float resultY)
    {
        resultX = preferredX;
        resultY = preferredY;

        if (NavMeshHandler.instance == null) return false;

        Vector2 center = new Vector2(centerX, centerY);
        Vector2 dir = new Vector2(preferredX - centerX, preferredY - centerY);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right; // troop is (near) exactly on the building's center
        dir.Normalize();

        const float degreesPerSample = 360f / AngleSamples;

        for (int radiusStep = 0; radiusStep <= MaxRadiusSteps; radiusStep++)
        {
            float radius = blockRadius + PushMargin + radiusStep * RadiusStep;

            for (int i = 0; i < AngleSamples; i++)
            {
                float sign = (i % 2 == 0) ? 1f : -1f;
                int magnitude = (i + 1) / 2;
                float angleRad = sign * magnitude * degreesPerSample * Mathf.Deg2Rad;

                Vector2 candidate = center + Rotate(dir, angleRad) * radius;
                if (!IsWalkable(candidate.x, candidate.y)) continue;

                resultX = candidate.x;
                resultY = candidate.y;
                return true;
            }
        }

        return false;
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    private static bool IsWalkable(float x, float y)
        => NavMeshHandler.instance.GetNodeAtWorldCoords(x, y) != null;
}
