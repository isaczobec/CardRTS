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

    // Unlike most stat modifiers in this codebase (e.g. SpeedBoostCard), Chilled
    // deliberately does NOT stack — a target already chilled just has its existing
    // modifier's duration reset back to full instead of a hit adding its own separate
    // modifier entity (which StatModifierSystem would otherwise sum, stacking the slow
    // indefinitely on a target hit repeatedly).
    private static void ApplySlow(ECS ecs, ProjectileOnHitComponent onHit, ulong ownerId, ulong targetId)
    {
        ulong existingModifierId = FindActiveChilledModifierId(ecs, targetId);
        if (existingModifierId != 0)
        {
            ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
            ref ModifierComponent existing = ref modifierStore.GetComponent(existingModifierId);
            existing.TicksRemaining = TickManager.SecondsToTicks(onHit.DurationSeconds);
            ecs.Delta.MarkComponentDirty(existingModifierId, typeof(ModifierComponent));
            return;
        }

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

    // Public so anything else that cares whether a target is chilled (e.g. AbilityManager's
    // Ice Nova ability) can reuse this instead of duplicating it.
    public static bool HasActiveChilledModifier(ECS ecs, ulong targetId) => FindActiveChilledModifierId(ecs, targetId) != 0;

    // Deletes every active Chilled modifier targeting targetId — server-only, mirroring
    // ModifierSystem's own natural-expiry deletion (predicted-only deletion would desync a
    // client from the server's authoritative entity set). Public for AbilityManager's Ice
    // Nova ability, which consumes/removes the chill when upgrading a target to Frozen.
    public static void RemoveActiveChilledModifiers(ECS ecs, ulong targetId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore == null) return;

        _removalScratch.Clear();
        modifierStore.ForEach((ulong modifierId) =>
        {
            ModifierComponent modifier = modifierStore.GetComponent(modifierId);
            if (modifier.ModifierID != ModifierID.Chilled) return;
            if (modifier.TargetEntityId != targetId) return;
            _removalScratch.Add(modifierId);
        });

        foreach (ulong modifierId in _removalScratch)
            ecs.DeleteEntity(modifierId);
    }

    // Mirrors ActionWindupSystem.IsWindingUp's "walk every ModifierComponent targeting this
    // entity" shape.
    private static ulong FindActiveChilledModifierId(ECS ecs, ulong targetId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore == null) return 0;

        ulong found = 0;
        modifierStore.ForEach((ulong modifierId) =>
        {
            if (found != 0) return;
            ModifierComponent modifier = modifierStore.GetComponent(modifierId);
            if (modifier.ModifierID != ModifierID.Chilled) return;
            if (modifier.TargetEntityId != targetId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            found = modifierId;
        });
        return found;
    }

    private static readonly List<ulong> _removalScratch = new List<ulong>();
}
