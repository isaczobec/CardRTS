using System;
using System.Collections.Generic;

// Dispatches a pooled projectile's on-hit effect (see ProjectileOnHitComponent) by its
// ProjectileOnHitEffectType, mapping each type to the lambda that actually implements it.
// Subscribed to ProjectileHitRequest's "executed" callback (SubscribeExecuted — fires once
// Execute has actually run, i.e. the hit's DamageRequest is confirmed enqueued) rather than
// applying the effect directly from SeekingProjectileSystem/SkillshotProjectileSystem, so
// the on-hit effect and the damage it rides alongside both go through the same request
// pipeline. Registered as a GlobalSystem purely for the Setup hook, same as
// ArmorMitigationSystem.
public static class ProjectileOnHitSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static readonly Dictionary<ProjectileOnHitEffectType, Action<ECS, ProjectileOnHitComponent, ulong, ulong>> _effects
        = new Dictionary<ProjectileOnHitEffectType, Action<ECS, ProjectileOnHitComponent, ulong, ulong>>
    {
        { ProjectileOnHitEffectType.Slow, ApplySlow },
    };

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
    {
        ecs.Requests.SubscribeExecuted<ProjectileHitRequest>(OnProjectileHitExecuted);
    }

    // No-op if the projectile that caused this hit has no ProjectileOnHitComponent, or its
    // EffectType is None/unregistered.
    private static void OnProjectileHitExecuted(ProjectileHitRequest request, ECS ecs)
    {
        ComponentStore<ProjectileOnHitComponent> store = ecs.GetComponentStore<ProjectileOnHitComponent>();
        if (store == null || !store.HasComponent(request.ProjectileId)) return;

        ProjectileOnHitComponent onHit = store.GetComponent(request.ProjectileId);
        if (_effects.TryGetValue(onHit.EffectType, out Action<ECS, ProjectileOnHitComponent, ulong, ulong> effect))
            effect(ecs, onHit, request.OwnerId, request.TargetId);
    }

    // A fresh modifier entity per hit (see ModifierComponent's own doc comment on why a
    // buff/debuff is its own entity) — StatModifierSystem sums every active modifier
    // targeting an entity, so repeated hits stack rather than refreshing/replacing a single
    // one, same as every other stat modifier in this codebase (e.g. SpeedBoostCard).
    private static void ApplySlow(ECS ecs, ProjectileOnHitComponent onHit, ulong ownerId, ulong targetId)
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = targetId,
            TicksRemaining = TickManager.SecondsToTicks(onHit.DurationSeconds),
            ModifierID     = ModifierID.Chilled,
        });
        ecs.AddComponent(modifier.Id, new StatModifierComponent
        {
            SpeedRatioBonus = onHit.SlowRatio,
        });
        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.Chilled,
        });
    }
}
