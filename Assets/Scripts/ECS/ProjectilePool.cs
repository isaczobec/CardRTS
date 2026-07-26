using UnityEngine;

// Shared "pool based" projectile-firing helper for any troop with a ProjectileOwnerComponent
// pool of ProjectileBaseComponent entities (see those types for how the pool ring works).
// Finds the next available (non-active) pooled projectile starting from the owner's
// NextProjectileId, activates it aimed from a given firing position, and advances the
// owner's pointer past it so the following shot starts searching from there.
public static class ProjectilePool
{
    // Fallback for StatsQuery.GetRange when a skillshot's owner somehow has no
    // StatsComponent — matches SeekingProjectileSystem/DamageAuraSystem's own fallback
    // convention of a small sane default rather than 0.
    private const int DefaultRange = 10;

    // Returns the activated projectile's entity ID, or 0 if the troop has no projectile
    // pool or every pooled projectile is currently in flight. For a pooled
    // SeekingProjectileComponent, homes in on targetId. For a pooled
    // SkillshotProjectileComponent, aims once at targetId's position at the moment of
    // firing (never re-homes) — targetId itself isn't stored anywhere on that projectile.
    // rangeOverride, when > 0, overrides a skillshot-type pool's own travel distance for
    // this shot instead of deriving it from the owner's own Range stat — see AimSkillshot.
    public static ulong Fire(ECS ecs, ulong ownerId, ulong targetId, Vector2 firePosition, float rangeOverride = 0f)
    {
        ulong projectileId = ActivateNext(ecs, ownerId);
        if (projectileId == 0) return 0;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        ComponentStore<SeekingProjectileComponent> seekingStore = ecs.GetComponentStore<SeekingProjectileComponent>();
        if (seekingStore != null && seekingStore.HasComponent(projectileId))
        {
            ref SeekingProjectileComponent seeking = ref seekingStore.GetComponent(projectileId);
            seeking.TargetEntityId = targetId;
            ecs.Delta.MarkComponentDirty(projectileId, typeof(SeekingProjectileComponent));
        }

        ComponentStore<SkillshotProjectileComponent> skillshotStore = ecs.GetComponentStore<SkillshotProjectileComponent>();
        if (skillshotStore != null && skillshotStore.HasComponent(projectileId))
        {
            Vector2 direction = Vector2.right;
            if (posStore != null && posStore.HasComponent(targetId))
            {
                PositionComponent targetPos = posStore.GetComponent(targetId);
                Vector2 toTarget = new Vector2(targetPos.X, targetPos.Y) - firePosition;
                if (toTarget.sqrMagnitude > 0.0001f) direction = toTarget.normalized;
            }
            AimSkillshot(ecs, projectileId, ownerId, skillshotStore, direction, rangeOverride);
        }

        PlaceAndAnnounce(ecs, projectileId, ownerId, targetId, firePosition, posStore);
        return projectileId;
    }

    // Same as Fire, but for a fixed direction instead of a target entity — e.g. an ability
    // that fires a ring of shots outward with nothing to aim at. Only meaningful for a
    // pooled SkillshotProjectileComponent (a homing SeekingProjectileComponent fired this
    // way has no target, so it deactivates itself the next tick — see
    // SeekingProjectileSystem). rangeOverride, when > 0, overrides the pool's own travel
    // distance for this shot instead of deriving it from the owner's own Range stat — needed
    // when the pool's owner (see ProjectileOwnerIndex/ResolveOwnerAtIndex) is the caster
    // itself, whose Range stat means something else entirely (e.g. PirateCard's Hook, fired
    // from the Pirate's own melee-range pool but meant to travel much further — see
    // AbilityManager.BuildHookAbility, the only caller that sets this).
    public static ulong FireInDirection(ECS ecs, ulong ownerId, Vector2 direction, Vector2 firePosition, float rangeOverride = 0f)
    {
        ulong projectileId = ActivateNext(ecs, ownerId);
        if (projectileId == 0) return 0;

        ComponentStore<SkillshotProjectileComponent> skillshotStore = ecs.GetComponentStore<SkillshotProjectileComponent>();
        if (skillshotStore != null && skillshotStore.HasComponent(projectileId))
            AimSkillshot(ecs, projectileId, ownerId, skillshotStore, direction, rangeOverride);

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        PlaceAndAnnounce(ecs, projectileId, ownerId, 0, firePosition, posStore);
        return projectileId;
    }

    // Pre-allocates a ring of `count` homing (SeekingProjectileComponent) pooled projectile
    // entities for a troop. Returns the id of the first one — pass it as
    // ProjectileOwnerComponent.NextProjectileId when adding that component to the owner
    // (the caller's job, since only it knows whether the owner already has one).
    // renderableType defaults to the ordinary SeekingProjectile visual; pass a different one
    // for a visually distinct pool (e.g. IceManCard's chilling shots). onHit is null by
    // default (no on-hit effect beyond the normal DamageRequest); pass a
    // ProjectileOnHitComponent for a pool whose hits should also apply one (see
    // ProjectileOnHitSystem).
    public static ulong CreatePool(ECS ecs, ulong ownerId, int count, int speedMilliTilesPerSecond,
        RenderableType renderableType = RenderableType.SeekingProjectile, ProjectileOnHitComponent? onHit = null)
        => CreatePoolRing(ecs, ownerId, count, (e, id) =>
        {
            e.AddComponent(id, new RenderableComponent { Type = renderableType });
            e.AddComponent(id, new SeekingProjectileComponent { Speed = speedMilliTilesPerSecond });
            if (onHit.HasValue)
                e.AddComponent(id, onHit.Value);
        });

    // Same pooling scheme as CreatePool, but for skillshot (straight-line, radius-hit,
    // piercing) projectiles instead of homing ones — see SkillshotProjectileComponent.
    // renderableType defaults to the ordinary SkillshotProjectile visual; pass a different
    // one for a second pool of visually distinct projectiles (e.g. a faster/longer-range
    // kind fired by an ability — see SkillshotRangedTroopCard). damageMultiplier defaults
    // to 1 (the owning troop's plain Damage stat); pass a different one for a pool that
    // should hit harder/softer than the troop's ordinary auto-attack. onHit is null by
    // default (no on-hit effect beyond the normal DamageRequest); pass a
    // ProjectileOnHitComponent for a pool whose hits should also apply one (see
    // ProjectileOnHitSystem) — mirrors CreatePool's own onHit param. SkillshotProjectileSystem
    // creates the same ProjectileHitRequest CreatePool's SeekingProjectileSystem does per hit,
    // so ProjectileOnHitSystem's dispatch works identically for either pool kind.
    // stopOnFirstHit defaults to false (every existing skillshot pool's own piercing
    // behavior); pass true for a pool that should stop dead on the first thing it hits
    // instead — see SkillshotProjectileComponent.StopOnFirstHit (PirateCard's Hook).
    public static ulong CreateSkillshotPool(ECS ecs, ulong ownerId, int count, int speedMilliTilesPerSecond, float hitRadius,
        RenderableType renderableType = RenderableType.SkillshotProjectile, float damageMultiplier = 1f, ProjectileOnHitComponent? onHit = null,
        bool stopOnFirstHit = false)
        => CreatePoolRing(ecs, ownerId, count, (e, id) =>
        {
            e.AddComponent(id, new RenderableComponent { Type = renderableType });
            e.AddComponent(id, new SkillshotProjectileComponent
            {
                Speed           = speedMilliTilesPerSecond,
                HitRadius       = hitRadius,
                DamageMultiplier = damageMultiplier,
                StopOnFirstHit  = stopOnFirstHit,
            });
            if (onHit.HasValue)
                e.AddComponent(id, onHit.Value);
        });

    // Finds the next available pooled projectile, activates it, and advances the owner's
    // search pointer past it. Shared by Fire/FireInDirection; callers still need to aim
    // (kind-specific) and place/announce it themselves.
    private static ulong ActivateNext(ECS ecs, ulong ownerId)
    {
        ComponentStore<ProjectileOwnerComponent> ownerStore = ecs.GetComponentStore<ProjectileOwnerComponent>();
        ComponentStore<ProjectileBaseComponent> projectileStore = ecs.GetComponentStore<ProjectileBaseComponent>();
        if (ownerStore == null || projectileStore == null || !ownerStore.HasComponent(ownerId)) return 0;

        ref ProjectileOwnerComponent owner = ref ownerStore.GetComponent(ownerId);
        ulong projectileId = FindAvailable(projectileStore, owner.NextProjectileId, owner.MaxProjectiles);
        if (projectileId == 0) return 0;

        ref ProjectileBaseComponent projectile = ref projectileStore.GetComponent(projectileId);
        projectile.IsActive = true;
        owner.NextProjectileId = projectile.NextProjectileId;
        ecs.Delta.MarkComponentDirty(projectileId, typeof(ProjectileBaseComponent));
        ecs.Delta.MarkComponentDirty(ownerId, typeof(ProjectileOwnerComponent));

        return projectileId;
    }

    private static void AimSkillshot(ECS ecs, ulong projectileId, ulong ownerId, ComponentStore<SkillshotProjectileComponent> skillshotStore, Vector2 direction, float rangeOverride = 0f)
    {
        ref SkillshotProjectileComponent skillshot = ref skillshotStore.GetComponent(projectileId);

        if (direction.sqrMagnitude <= 0.0001f) direction = Vector2.right;
        else direction.Normalize();

        skillshot.DirectionX = direction.x;
        skillshot.DirectionY = direction.y;
        skillshot.RangeRemaining = rangeOverride > 0f ? rangeOverride : StatsQuery.GetRange(ecs, ownerId, DefaultRange);
        ecs.Delta.MarkComponentDirty(projectileId, typeof(SkillshotProjectileComponent));
    }

    private static void PlaceAndAnnounce(ECS ecs, ulong projectileId, ulong ownerId, ulong targetId, Vector2 firePosition, ComponentStore<PositionComponent> posStore)
    {
        if (posStore != null && posStore.HasComponent(projectileId))
        {
            ref PositionComponent pos = ref posStore.GetComponent(projectileId);
            pos.X = firePosition.x;
            pos.Y = firePosition.y;
            ecs.Delta.MarkComponentDirty(projectileId, typeof(PositionComponent));
        }

        ecs.FlagEvents.Add(new ProjectileActivatedEvent { EntityId = projectileId, OwnerEntityId = ownerId, TargetEntityId = targetId, X = firePosition.x, Y = firePosition.y });
    }

    // Shared ring-building loop for CreatePool/CreateSkillshotPool: every pooled projectile
    // needs a PositionComponent + ProjectileBaseComponent (linked into the ring) regardless
    // of kind — addKindComponents adds whatever's specific to that kind (Renderable +
    // Seeking/Skillshot).
    private static ulong CreatePoolRing(ECS ecs, ulong ownerId, int count, System.Action<ECS, ulong> addKindComponents)
    {
        ComponentStore<ProjectileBaseComponent> projectileStore = ecs.GetComponentStore<ProjectileBaseComponent>();

        ulong firstId = 0;
        ulong previousId = 0;

        for (int i = 0; i < count; i++)
        {
            EntityHandle projectile = ecs.CreateEntity();
            ulong id = projectile.Id;
            if (i == 0) firstId = id;

            ecs.AddComponent(id, new PositionComponent(0f, 0f));
            addKindComponents(ecs, id);
            ecs.AddComponent(id, new ProjectileBaseComponent { OwnerEntityId = ownerId, IsActive = false });

            if (previousId != 0)
            {
                ref ProjectileBaseComponent previous = ref projectileStore.GetComponent(previousId);
                previous.NextProjectileId = id;
            }

            previousId = id;
        }

        ref ProjectileBaseComponent last = ref projectileStore.GetComponent(previousId);
        last.NextProjectileId = firstId;

        return firstId;
    }

    // Walks a troop's projectile-pool-owner chain (see ProjectileOwnerComponent's own doc
    // comment) starting from casterId — index 0 is casterId itself (its own primary pool),
    // index 1 is casterId's NextProjectileOwnerId, and so on. Returns 0 if the chain doesn't
    // reach that far (e.g. index 1 requested but casterId has no second pool linked).
    public static ulong ResolveOwnerAtIndex(ECS ecs, ulong casterId, int index)
    {
        ComponentStore<ProjectileOwnerComponent> ownerStore = ecs.GetComponentStore<ProjectileOwnerComponent>();
        if (ownerStore == null) return 0;

        ulong currentId = casterId;
        for (int i = 0; i < index; i++)
        {
            if (currentId == 0 || !ownerStore.HasComponent(currentId)) return 0;
            currentId = ownerStore.GetComponent(currentId).NextProjectileOwnerId;
        }

        return currentId;
    }

    private static ulong FindAvailable(ComponentStore<ProjectileBaseComponent> projectileStore, ulong startId, int maxProjectiles)
    {
        ulong currentId = startId;
        int limit = Mathf.Max(maxProjectiles, 1);

        for (int i = 0; i < limit && currentId != 0; i++)
        {
            if (!projectileStore.HasComponent(currentId)) return 0;

            ProjectileBaseComponent projectile = projectileStore.GetComponent(currentId);
            if (!projectile.IsActive) return currentId;

            currentId = projectile.NextProjectileId;
        }

        return 0;
    }
}
