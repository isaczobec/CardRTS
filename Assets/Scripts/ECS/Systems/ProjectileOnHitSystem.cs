using System;
using System.Collections.Generic;
using UnityEngine;

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
        { ProjectileOnHitEffectType.Burn, ApplyBurn },
        { ProjectileOnHitEffectType.Aoe, ApplyAoe },
        { ProjectileOnHitEffectType.Hook, ApplyHook },
    };

    // Scratch for ApplyBurn's splash query, reused across every burn hit rather than
    // reallocated per hit.
    private static readonly List<ulong> _burnSplashBuffer = new List<ulong>();

    // Scratch for ApplyAoe's blast-radius query, reused across every hit rather than
    // reallocated per hit.
    private static readonly List<ulong> _aoeBuffer = new List<ulong>();

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
        ulong existingModifierId = FindActiveModifierId(ecs, targetId, ModifierID.Chilled);
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
    public static bool HasActiveChilledModifier(ECS ecs, ulong targetId) => FindActiveModifierId(ecs, targetId, ModifierID.Chilled) != 0;

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

    // FireManCard's burn — refreshes/stacks a Scorched debuff (ModifierComponent +
    // StackingBurnDebuffComponent + DamageOverTimeComponent + RenderableModifierComponent)
    // on the primary hit target, then splashes the same treatment onto every other enemy
    // troop within onHit.BurnSplashRangeMultiplier x the shooter's own Range stat of the hit
    // point — mirrors AbilityManager's Ice Nova AOE query, just centered on the impact
    // rather than the caster.
    private static void ApplyBurn(ECS ecs, ProjectileOnHitComponent onHit, ulong ownerId, ulong targetId)
    {
        ApplyScorch(ecs, onHit, ownerId, targetId);

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (posStore == null || troopStore == null || healthStore == null) return;
        if (!posStore.HasComponent(targetId) || !troopStore.HasComponent(ownerId)) return;

        PositionComponent targetPos = posStore.GetComponent(targetId);
        ushort casterOwnerId = troopStore.GetComponent(ownerId).OwnerPlayerId;
        float radius = StatsQuery.GetRange(ecs, ownerId, BurnFallbackRange) * onHit.BurnSplashRangeMultiplier;

        _burnSplashBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(targetPos.X, targetPos.Y, radius, _burnSplashBuffer);

        foreach (ulong splashTargetId in _burnSplashBuffer)
        {
            if (splashTargetId == targetId || splashTargetId == ownerId) continue;
            if (!troopStore.HasComponent(splashTargetId)) continue;
            if (troopStore.GetComponent(splashTargetId).OwnerPlayerId == casterOwnerId) continue;
            if (!healthStore.HasComponent(splashTargetId)) continue;
            if (!ActivationQuery.IsActivated(ecs, splashTargetId)) continue;

            ApplyScorch(ecs, onHit, ownerId, splashTargetId);
        }
    }

    // Applies/refreshes the Scorched debuff on a single target — shared by ApplyBurn's
    // primary hit and its splash targets, so both go through identical stacking/refresh
    // logic. Stacks starts at 0 on a fresh application (base damage only) and increments
    // (capped at onHit.BurnMaxStacks) on every refresh from a later hit — see
    // StackingBurnDebuffComponent's own doc comment.
    private static void ApplyScorch(ECS ecs, ProjectileOnHitComponent onHit, ulong ownerId, ulong targetId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<StackingBurnDebuffComponent> stackStore = ecs.GetComponentStore<StackingBurnDebuffComponent>();
        ComponentStore<DamageOverTimeComponent> dotStore = ecs.GetComponentStore<DamageOverTimeComponent>();

        ulong modifierId = FindActiveModifierId(ecs, targetId, ModifierID.Scorched);
        int stacks;

        if (modifierId != 0)
        {
            ref ModifierComponent modifier = ref modifierStore.GetComponent(modifierId);
            modifier.TicksRemaining = TickManager.SecondsToTicks(onHit.DurationSeconds);
            ecs.Delta.MarkComponentDirty(modifierId, typeof(ModifierComponent));

            ref StackingBurnDebuffComponent stack = ref stackStore.GetComponent(modifierId);
            stack.Stacks = Math.Min(stack.Stacks + 1, stack.MaxStacks);
            ecs.Delta.MarkComponentDirty(modifierId, typeof(StackingBurnDebuffComponent));
            stacks = stack.Stacks;
        }
        else
        {
            EntityHandle modifier = ecs.CreateEntity();
            modifierId = modifier.Id;
            stacks = 0;

            ecs.AddComponent(modifierId, new ModifierComponent
            {
                TargetEntityId = targetId,
                TicksRemaining = TickManager.SecondsToTicks(onHit.DurationSeconds),
                ModifierID     = ModifierID.Scorched,
            });
            ecs.AddComponent(modifierId, new StackingBurnDebuffComponent
            {
                Stacks    = stacks,
                MaxStacks = onHit.BurnMaxStacks,
            });
            ecs.AddComponent(modifierId, new RenderableModifierComponent { Type = RenderableModifierType.Scorched });

            int periodTicks = Math.Max(1, onHit.BurnProcPeriodTicks);
            ecs.AddComponent(modifierId, new DamageOverTimeComponent
            {
                PeriodTicks        = periodTicks,
                TicksUntilNextProc = periodTicks,
                DealerEntityId     = ownerId,
            });
        }

        int damagePerProc = Mathf.RoundToInt(onHit.BurnBaseDamagePerProc
            + stacks * onHit.BurnDamagePerStackRatio * StatsQuery.GetDamage(ecs, ownerId, BurnFallbackDamage));

        ref DamageOverTimeComponent dot = ref dotStore.GetComponent(modifierId);
        dot.DamagePerProc = damagePerProc;
        ecs.Delta.MarkComponentDirty(modifierId, typeof(DamageOverTimeComponent));
    }

    // SantaClausCard's on-hit effect: deals instant AOE damage to every enemy troop within
    // AoeRadiusMultiplier x the shooter's own Range stat of the hit point (including the
    // primary target itself, which also still takes the normal per-shot DamageRequest every
    // projectile deals regardless of EffectType — see ProjectileHitRequest.Execute — so the
    // troop standing at ground zero effectively takes both). Mirrors ApplyBurn's splash
    // query, just dealing direct damage instead of applying a modifier.
    private static void ApplyAoe(ECS ecs, ProjectileOnHitComponent onHit, ulong ownerId, ulong targetId)
    {
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (posStore == null || troopStore == null || healthStore == null) return;
        if (!posStore.HasComponent(targetId) || !troopStore.HasComponent(ownerId)) return;

        PositionComponent hitPos = posStore.GetComponent(targetId);
        ushort casterOwnerId = troopStore.GetComponent(ownerId).OwnerPlayerId;
        float radius = StatsQuery.GetRange(ecs, ownerId, AoeFallbackRange) * onHit.AoeRadiusMultiplier;
        int aoeDamage = Mathf.RoundToInt(StatsQuery.GetDamage(ecs, ownerId, AoeFallbackDamage) * onHit.AoeDamageRatio);
        if (aoeDamage <= 0) return;

        _aoeBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(hitPos.X, hitPos.Y, radius, _aoeBuffer);

        foreach (ulong splashTargetId in _aoeBuffer)
        {
            if (splashTargetId == ownerId) continue;
            if (!troopStore.HasComponent(splashTargetId)) continue;
            if (troopStore.GetComponent(splashTargetId).OwnerPlayerId == casterOwnerId) continue;
            if (!healthStore.HasComponent(splashTargetId)) continue;
            if (!ActivationQuery.IsActivated(ecs, splashTargetId)) continue;

            // Enqueued (not flushed here) — same reasoning as ProjectileHitRequest.Execute's
            // own DamageRequest: DamageResolutionSystem flushes DamageRequest later this same
            // tick, after every other DamageRequest subscriber (ArmorMitigationSystem, ...)
            // has had a chance to run.
            ecs.Requests.CreateRequest(new DamageRequest(splashTargetId, aoeDamage)
            {
                DealerEntityId = ownerId,
                ProcType       = DamageProcType.Secondary,
            });
        }
    }

    // Fallbacks for StatsQuery.GetRange/GetDamage when the shooter somehow has no
    // StatsComponent — matches BurnFallbackRange/BurnFallbackDamage's own convention.
    private const int AoeFallbackRange = 10;
    private const int AoeFallbackDamage = 10;

    // PirateCard's Hook ability on-hit effect: yanks the hit target via
    // DisplacementSystem.BeginDisplacement to a point HookPullBehindOffset world units past
    // the shooter (ownerId), on the far side from the target — i.e. "slightly behind" the
    // shooter as seen from the target's own approach direction — computed from the shooter's
    // CURRENT position at the moment of the hit, not wherever it stood when the hook was
    // fired. The pull itself takes HookPullDurationSeconds (converted to a straight-line
    // velocity, same primitive AbilityManager's own Push/GroundSlam abilities use).
    private static void ApplyHook(ECS ecs, ProjectileOnHitComponent onHit, ulong ownerId, ulong targetId)
    {
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        if (posStore == null || movStore == null) return;
        if (!posStore.HasComponent(ownerId) || !posStore.HasComponent(targetId)) return;
        if (!movStore.HasComponent(targetId)) return; // nothing to displace (e.g. a building)

        PositionComponent ownerPos = posStore.GetComponent(ownerId);
        PositionComponent targetPos = posStore.GetComponent(targetId);
        Vector2 ownerVec = new Vector2(ownerPos.X, ownerPos.Y);
        Vector2 targetVec = new Vector2(targetPos.X, targetPos.Y);

        Vector2 pullDirection = ownerVec - targetVec;
        // Owner and target exactly overlapping is the only way this is zero — an arbitrary
        // but deterministic fallback direction beats a NaN from normalizing a zero vector,
        // same reasoning as AbilityManager's own Push/GroundSlam abilities.
        pullDirection = pullDirection.sqrMagnitude > 0.0001f ? pullDirection.normalized : Vector2.right;

        Vector2 landingPoint = ownerVec + pullDirection * onHit.HookPullBehindOffset;

        float duration = Mathf.Max(0.01f, onHit.HookPullDurationSeconds);
        Vector2 velocity = (landingPoint - targetVec) / duration;
        int ticks = Mathf.Max(1, TickManager.SecondsToTicks(duration));

        DisplacementSystem.BeginDisplacement(ecs, targetId, velocity.x, velocity.y, ticks);
    }

    // Mirrors ActionWindupSystem.IsWindingUp's "walk every ModifierComponent targeting this
    // entity" shape. Shared by every ModifierID this system cares about (Chilled, Scorched)
    // rather than one copy per id.
    private static ulong FindActiveModifierId(ECS ecs, ulong targetId, ModifierID modifierId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore == null) return 0;

        ulong found = 0;
        modifierStore.ForEach((ulong candidateId) =>
        {
            if (found != 0) return;
            ModifierComponent modifier = modifierStore.GetComponent(candidateId);
            if (modifier.ModifierID != modifierId) return;
            if (modifier.TargetEntityId != targetId) return;
            if (!ModifierQuery.IsActive(ecs, candidateId)) return;
            found = candidateId;
        });
        return found;
    }

    // Fallbacks for StatsQuery.GetRange/GetDamage when the shooter somehow has no
    // StatsComponent — matches ProjectilePool/AbilityManager's own small-sane-default
    // convention rather than 0.
    private const int BurnFallbackRange = 10;
    private const int BurnFallbackDamage = 10;

    private static readonly List<ulong> _removalScratch = new List<ulong>();
}
