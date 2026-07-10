using UnityEngine;

// Shared "pool based" projectile-firing helper for any troop with a ProjectileOwnerComponent
// pool of ProjectileBaseComponent entities (see those types for how the pool ring works).
// Finds the next available (non-active) pooled projectile starting from the owner's
// NextProjectileId, activates it aimed at a target from a given firing position, and
// advances the owner's pointer past it so the following shot starts searching from there.
public static class ProjectilePool
{
    // Returns the activated projectile's entity ID, or 0 if the troop has no projectile
    // pool or every pooled projectile is currently in flight.
    public static ulong Fire(ECS ecs, ulong ownerId, ulong targetId, Vector2 firePosition)
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

        ComponentStore<SeekingProjectileComponent> seekingStore = ecs.GetComponentStore<SeekingProjectileComponent>();
        if (seekingStore != null && seekingStore.HasComponent(projectileId))
        {
            ref SeekingProjectileComponent seeking = ref seekingStore.GetComponent(projectileId);
            seeking.TargetEntityId = targetId;
            ecs.Delta.MarkComponentDirty(projectileId, typeof(SeekingProjectileComponent));
        }

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore != null && posStore.HasComponent(projectileId))
        {
            ref PositionComponent pos = ref posStore.GetComponent(projectileId);
            pos.X = firePosition.x;
            pos.Y = firePosition.y;
            ecs.Delta.MarkComponentDirty(projectileId, typeof(PositionComponent));
        }

        ecs.FlagEvents.Add(new ProjectileActivatedEvent { EntityId = projectileId, OwnerEntityId = ownerId, TargetEntityId = targetId });
        return projectileId;
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
