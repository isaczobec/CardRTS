using UnityEngine;

// Each tick, every currently-active (in-flight) SeekingProjectileComponent steps directly
// toward its target's current position (no pathfinding — this is a homing shot, not
// something that navigates around obstacles). Once within one tile of the target it deals
// damage (via the owning troop's Damage stat) and deactivates back into its owner's pool
// — see ProjectileBaseComponent/ProjectileOwnerComponent.
// Stateless, so registered per ECS the same way DeathSystem/DamageResolutionSystem are.
// Movement and the resulting DamageRequest run unconditionally (predicted on clients, same
// as BasicMeleeAISystem's damage) — only entity deletion needs to be server-only, and this
// system never deletes anything; ProjectilePoolCleanupSystem does that separately.
public static class SeekingProjectileSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const float ImpactRadius = 1f;
    private const int DefaultDamage = 10;

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<SeekingProjectileComponent> seekingStore = ecs.GetComponentStore<SeekingProjectileComponent>();
        ComponentStore<ProjectileBaseComponent> projectileStore = ecs.GetComponentStore<ProjectileBaseComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        seekingStore.ForEach((ulong id) => Tick(id, ecs, seekingStore, projectileStore, posStore));
    }

    private static void Tick(ulong id, ECS ecs,
        ComponentStore<SeekingProjectileComponent> seekingStore,
        ComponentStore<ProjectileBaseComponent> projectileStore,
        ComponentStore<PositionComponent> posStore)
    {
        if (!projectileStore.HasComponent(id) || !posStore.HasComponent(id)) return;

        ref ProjectileBaseComponent projectile = ref projectileStore.GetComponent(id);
        if (!projectile.IsActive) return;

        SeekingProjectileComponent seeking = seekingStore.GetComponent(id);
        ulong targetId = seeking.TargetEntityId;

        if (!ecs.HasEntity(targetId) || !posStore.HasComponent(targetId))
        {
            Deactivate(ecs, id, ref projectile);
            return;
        }

        ref PositionComponent pos = ref posStore.GetComponent(id);
        PositionComponent targetPos = posStore.GetComponent(targetId);
        Vector2 current = new Vector2(pos.X, pos.Y);
        Vector2 target = new Vector2(targetPos.X, targetPos.Y);

        if (Vector2.Distance(current, target) <= ImpactRadius)
        {
            int damage = StatsQuery.GetDamage(ecs, projectile.OwnerEntityId, DefaultDamage);
            ecs.Requests.CreateRequest(new DamageRequest(targetId, damage) { DealerEntityId = projectile.OwnerEntityId });
            Deactivate(ecs, id, ref projectile);
            return;
        }

        // Speed is stored in milli-tiles/second (see SeekingProjectileComponent); convert
        // to tiles/second, then to this tick's step distance via TickManager rather than
        // reading TickInterval directly, matching the seconds->ticks conversion idiom used
        // throughout the rest of the codebase.
        float tilesPerSecond = seeking.Speed / 1000f;
        float step = tilesPerSecond * TickManager.TicksToSeconds(1f);

        Vector2 next = Vector2.MoveTowards(current, target, step);
        pos.X = next.x;
        pos.Y = next.y;
        ecs.Delta.MarkComponentDirty(id, typeof(PositionComponent));
    }

    private static void Deactivate(ECS ecs, ulong id, ref ProjectileBaseComponent projectile)
    {
        projectile.IsActive = false;
        ecs.Delta.MarkComponentDirty(id, typeof(ProjectileBaseComponent));
        ecs.FlagEvents.Add(new ProjectileDeactivatedEvent { EntityId = id });
    }
}
