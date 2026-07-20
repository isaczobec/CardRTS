using System.Collections.Generic;
using UnityEngine;

// Each tick, every currently-active (in-flight) SkillshotProjectileComponent moves in a
// straight line along its own Direction — no homing, no pathfinding — and, unlike
// SeekingProjectileSystem's single-target impact, does a radius-based hit check
// (HitRadius) against every overlapping enemy, dealing damage via the owning troop's
// Damage stat to any that aren't currently hitbox-immune (HealthComponent.
// HitboxImmunityTicksRemaining) and then granting them fresh immunity (read off the
// owner's own HealthComponent.HitboxImmunityTicksToGive) so a piercing shot doesn't
// restack damage every single tick it overlaps the same target. Expires (deactivates back
// into its owner's pool) once RangeRemaining runs out rather than on first impact, so it
// can pierce through multiple targets over its flight.
//
// Direction/RangeRemaining are plain data on SkillshotProjectileComponent so a future
// component (e.g. one that curves a shot, or extends/shortens it mid-flight) can mutate
// them directly before this system runs each tick, without this system — or
// SkillshotProjectileComponent itself — needing to know why they changed.
//
// Stateless, so registered per ECS the same way SeekingProjectileSystem is. Movement and
// the resulting DamageRequests run unconditionally (predicted on clients) — only entity
// deletion needs to be server-only, and this system never deletes anything;
// ProjectilePoolCleanupSystem does that separately.
public static class SkillshotProjectileSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const int DefaultDamage = 10;

    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<SkillshotProjectileComponent> skillshotStore = ecs.GetComponentStore<SkillshotProjectileComponent>();
        ComponentStore<ProjectileBaseComponent> projectileStore = ecs.GetComponentStore<ProjectileBaseComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        skillshotStore.ForEach((ulong id) => Tick(id, ecs, skillshotStore, projectileStore, posStore));
    }

    private static void Tick(ulong id, ECS ecs,
        ComponentStore<SkillshotProjectileComponent> skillshotStore,
        ComponentStore<ProjectileBaseComponent> projectileStore,
        ComponentStore<PositionComponent> posStore)
    {
        if (!projectileStore.HasComponent(id) || !posStore.HasComponent(id)) return;

        ref ProjectileBaseComponent projectile = ref projectileStore.GetComponent(id);
        if (!projectile.IsActive) return;

        ref SkillshotProjectileComponent skillshot = ref skillshotStore.GetComponent(id);
        ref PositionComponent pos = ref posStore.GetComponent(id);

        Vector2 direction = new Vector2(skillshot.DirectionX, skillshot.DirectionY);
        if (direction.sqrMagnitude > 0.0001f) direction.Normalize();

        // Speed is stored in milli-tiles/second (see SkillshotProjectileComponent); convert
        // to tiles/second, then to this tick's step distance, matching
        // SeekingProjectileSystem's conversion idiom.
        float tilesPerSecond = skillshot.Speed / 1000f;
        float step = tilesPerSecond * TickManager.TicksToSeconds(1f);

        Vector2 next = new Vector2(pos.X, pos.Y) + direction * step;
        pos.X = next.x;
        pos.Y = next.y;
        ecs.Delta.MarkComponentDirty(id, typeof(PositionComponent));

        HitOverlappingEnemies(ecs, id, projectile.OwnerEntityId, next, skillshot.HitRadius);

        skillshot.RangeRemaining -= step;
        ecs.Delta.MarkComponentDirty(id, typeof(SkillshotProjectileComponent));

        if (skillshot.RangeRemaining <= 0f)
            Deactivate(ecs, id, ref projectile);
    }

    private static void HitOverlappingEnemies(ECS ecs, ulong projectileId, ulong ownerId, Vector2 pos, float hitRadius)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (troopStore == null || healthStore == null || !troopStore.HasComponent(ownerId)) return;

        ushort ownerPlayerId = troopStore.GetComponent(ownerId).OwnerPlayerId;
        int hitboxImmunityTicksToGive = healthStore.HasComponent(ownerId) ? healthStore.GetComponent(ownerId).HitboxImmunityTicksToGive : 0;
        int damage = StatsQuery.GetDamage(ecs, ownerId, DefaultDamage);

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(pos.x, pos.y, hitRadius, _queryBuffer);

        foreach (ulong targetId in _queryBuffer)
        {
            if (targetId == projectileId || targetId == ownerId) continue;
            if (!troopStore.HasComponent(targetId)) continue;
            if (troopStore.GetComponent(targetId).OwnerPlayerId == ownerPlayerId) continue;
            if (!healthStore.HasComponent(targetId)) continue;
            if (!ActivationQuery.IsActivated(ecs, targetId)) continue;

            ref HealthComponent targetHealth = ref healthStore.GetComponent(targetId);
            if (targetHealth.HitboxImmunityTicksRemaining > 0) continue;

            ecs.Requests.CreateRequest(new DamageRequest(targetId, damage) { DealerEntityId = ownerId });

            targetHealth.HitboxImmunityTicksRemaining = hitboxImmunityTicksToGive;
            ecs.Delta.MarkComponentDirty(targetId, typeof(HealthComponent));
        }
    }

    private static void Deactivate(ECS ecs, ulong id, ref ProjectileBaseComponent projectile)
    {
        projectile.IsActive = false;
        ecs.Delta.MarkComponentDirty(id, typeof(ProjectileBaseComponent));
        ecs.FlagEvents.Add(new ProjectileDeactivatedEvent { EntityId = id });
    }
}
