using System;
using System.Collections.Generic;

// Ranged support troop based on SkillshotRangedTroopCard ("the Ranger") — same homing
// auto-attack shape (BasicRangedAIComponent + a single ProjectilePool, see
// SkillshotRangedTroopCard) but with no ability/second projectile pool at all (explicit
// design ask: "should not have any abilities"), a much shorter Range, lower Damage, and a
// 60-second LifetimeComponent mirroring EphemeralSkeletonsCard's own pattern.
//
// Landing a hit grants (or refreshes) a 12-second self-buff — reuses OnHitScheduleComponent/
// OnHitScheduleSystem's existing "react to this troop dealing damage" wiring, scheduling
// ResolveHealAuraApply via ScheduledCallSystem. That self-buff carries a
// PeriodicAreaEffectComponent (see that class/PeriodicAreaEffectSystem) scanning for nearby
// FRIENDLY troops and applying/refreshing a heal-over-time modifier (HealModifierComponent +
// HealSourceComponent) on each one found — HealSourceComponent's HealerEntityId is what lets
// multiple Healer Guardians heal the same target simultaneously (each keeps its own separate
// modifier entity) instead of one overwriting another's.
public class HealerGuardianCard : SpawnAtPointCard
{
    // Unchanged from SkillshotRangedTroopCard ("the Ranger").
    private const int MaxHealth = 90;
    private const int Armor = 20;
    private const float AttackSpeedMilliseconds = 900f;
    private const int SpellResist = 0;

    // Explicit design ask — vs. the Ranger's own Speed 50 / Damage 26 / Range 21.
    private const int Speed = 36;
    private const int Damage = 5;
    private const int Range = 12;

    // Unchanged from SkillshotRangedTroopCard.
    private const float DetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 20f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 32;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec

    // Explicit design ask — mirrors EphemeralSkeletonsCard's own LifetimeComponent pattern
    // (counts down only once actually activated, see LifetimeSystem).
    private const float LifetimeSeconds = 60f;

    // How long the self-buff that lets this troop heal nearby allies lasts once granted —
    // explicit design ask ("gets a new modifier for 12 seconds"). Refreshed back to this full
    // duration on every subsequent qualifying hit (see OnHitScheduleComponent below), so
    // landing hits often enough (AttackSpeedMilliseconds is well under 12s) keeps it up
    // indefinitely in sustained combat.
    private const float HealAuraDurationSeconds = 12f;

    // How often the self-buff re-scans for nearby friendly troops to apply/refresh healing
    // on — see PeriodicAreaEffectSystem.
    private const float HealScanPeriodSeconds = 1f;

    // Multiple of this troop's own Range stat — explicit design ask ("some radius, multiple
    // of the troop's range stat").
    private const float HealRadiusMultiplier = 2.5f;

    // How much each heal-over-time proc restores on an affected ally — see HealRequest.
    private const float HealAdditivePerProc = 4f;
    private const float HealRatioPerProc = 0.02f;
    private const float HealPeriodSeconds = 1f;

    // How long an individual ally's own granted heal-over-time lasts before it needs
    // refreshing by another scan — kept longer than HealScanPeriodSeconds so a missed scan
    // or two doesn't let it lapse.
    private const float GrantedHealDurationSeconds = 4f;

    private const float MaxDistanceFromBuilding = 20f;
    // Explicit design ask — a support troop should be deployable alongside the friendly
    // troops it's meant to heal, not only near a building.
    private const float MaxDistanceFromTroop = 15f;

    public override int ShopGoldCost => 110;

    public override CardType Type => CardType.HealerGuardian;
    public override string Title => "Healer Guardian";
    public override string ImageName => "HealerGuardian";
    public override string Description => "A short-lived ranged support troop with no abilities. Landing a hit grants it a 12-second aura that periodically heals nearby friendly troops.";
    public override string IndicatorPrefabName => "HealerGuardian";

    // Carries its own LifetimeComponent (see LifetimeSeconds above) — a 60-second troop
    // doesn't count toward ResourceCollectorTrickleSystem's "does this player have a
    // permanent troop" check, so this card shouldn't either.
    public override bool CanCollectResources => false;

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 100,
            Wood = 30,
            Gems = 7,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;
    public override bool AllowsFriendlyTroopRange() => true;

    static HealerGuardianCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.HealAuraApplyResolve, ResolveHealAuraApply);
        PeriodicAreaEffectSystem.RegisterEffect(AreaEffectType.HealPulse, ApplyHealPulse);
    }

    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = Speed,
        Range       = Range,
        Armor       = Armor,
        Damage      = Damage,
        AttackSpeed = TickManager.MillisecondsToTicks(AttackSpeedMilliseconds),
        SpellResist = SpellResist,
    };

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        StatsComponent stats = BuildStats();

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.HealerGuardian, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicRangedAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                WindDownMultiplier       = WindDownMultiplier,
            }),
            (e, id) =>
            {
                ulong firstProjectileId = ProjectilePool.CreatePool(e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond, RenderableType.HealerGuardianProjectile);
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
            (e, id) =>
            {
                int lifetimeTicks = TickManager.SecondsToTicks(LifetimeSeconds);
                e.AddComponent(id, new LifetimeComponent
                {
                    TicksRemaining        = lifetimeTicks,
                    InitialTicksRemaining = lifetimeTicks,
                    ShowTimer              = true,
                });
            },
            // Innate trait, not a modifier — see OnHitScheduleComponent's own doc comment.
            // Fires on every qualifying hit (PeriodHits left at its 0/"every hit" default).
            // RequireEnemyOwnedHit — explicit design ask: the aura should trigger off an
            // enemy troop OR enemy building hit, just not a neutral one (e.g. a resource
            // node).
            (e, id) => e.AddComponent(id, new OnHitScheduleComponent
            {
                CallType           = ScheduledCallType.HealAuraApplyResolve,
                DelayTicks         = 0,
                RequireEnemyOwnedHit = true,
            }),
        });
    }

    // ── Heal aura self-buff: applied/refreshed on this troop whenever it lands a hit ────────

    private static void ResolveHealAuraApply(ECS ecs, ScheduledCallComponent call)
        => ApplyOrRefreshHealAura(ecs, call.Param0);

    private static void ApplyOrRefreshHealAura(ECS ecs, ulong healerId)
    {
        int durationTicks = TickManager.SecondsToTicks(HealAuraDurationSeconds);

        ulong existingId = FindActiveHealAuraModifierId(ecs, healerId);
        if (existingId != 0)
        {
            ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
            ref ModifierComponent modifier = ref modifierStore.GetComponent(existingId);
            modifier.TicksRemaining = durationTicks;
            ecs.Delta.MarkComponentDirty(existingId, typeof(ModifierComponent));
            return;
        }

        EntityHandle modifierEntity = ecs.CreateEntity();
        ecs.AddComponent(modifierEntity.Id, new ModifierComponent
        {
            TargetEntityId = healerId,
            TicksRemaining = durationTicks,
            ModifierID     = ModifierID.HealAura,
        });

        int scanPeriodTicks = TickManager.SecondsToTicks(HealScanPeriodSeconds);
        ecs.AddComponent(modifierEntity.Id, new PeriodicAreaEffectComponent
        {
            RangeMultiplier    = HealRadiusMultiplier,
            PeriodTicks        = scanPeriodTicks,
            TicksUntilNextProc = scanPeriodTicks,
            Targets            = AreaTargetFlags.Friendly,
            EffectType         = AreaEffectType.HealPulse,
            Param0             = HealAdditivePerProc,
            Param1             = HealRatioPerProc,
            Param2             = TickManager.SecondsToTicks(GrantedHealDurationSeconds),
        });
        ecs.AddComponent(modifierEntity.Id, new RenderableModifierComponent { Type = RenderableModifierType.HealAura });
    }

    private static ulong FindActiveHealAuraModifierId(ECS ecs, ulong healerId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<PeriodicAreaEffectComponent> areaStore = ecs.GetComponentStore<PeriodicAreaEffectComponent>();
        if (modifierStore == null || areaStore == null) return 0;

        ulong found = 0;
        areaStore.ForEach((ulong modifierId) =>
        {
            if (found != 0) return;
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != healerId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            found = modifierId;
        });
        return found;
    }

    // ── Heal pulse effect: registered against AreaEffectType.HealPulse ──────────────────────

    private static void ApplyHealPulse(ECS ecs, ulong healerId, ulong targetId, PeriodicAreaEffectComponent effect)
    {
        int grantedDurationTicks = effect.Param2;

        ulong existingHealModifierId = FindActiveHealModifierId(ecs, healerId, targetId);
        if (existingHealModifierId != 0)
        {
            ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
            ref ModifierComponent modifier = ref modifierStore.GetComponent(existingHealModifierId);
            modifier.TicksRemaining = grantedDurationTicks;
            ecs.Delta.MarkComponentDirty(existingHealModifierId, typeof(ModifierComponent));
            return;
        }

        EntityHandle modifierEntity = ecs.CreateEntity();
        ecs.AddComponent(modifierEntity.Id, new ModifierComponent
        {
            TargetEntityId = targetId,
            TicksRemaining = grantedDurationTicks,
            ModifierID     = ModifierID.Healing,
        });

        int healPeriodTicks = TickManager.SecondsToTicks(HealPeriodSeconds);
        ecs.AddComponent(modifierEntity.Id, new HealModifierComponent
        {
            PeriodTicks         = healPeriodTicks,
            TicksUntilNextProc  = healPeriodTicks,
            HealAdditivePerProc = effect.Param0,
            HealRatioPerProc    = effect.Param1,
        });
        ecs.AddComponent(modifierEntity.Id, new HealSourceComponent { HealerEntityId = healerId });
        ecs.AddComponent(modifierEntity.Id, new RenderableModifierComponent { Type = RenderableModifierType.Healing });
    }

    // Scoped by HealerEntityId (not just target) — this is what lets a DIFFERENT Healer
    // Guardian's own heal modifier on the same target stack alongside this one instead of
    // being refreshed/overwritten by it.
    private static ulong FindActiveHealModifierId(ECS ecs, ulong healerId, ulong targetId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<HealSourceComponent> sourceStore = ecs.GetComponentStore<HealSourceComponent>();
        if (modifierStore == null || sourceStore == null) return 0;

        ulong found = 0;
        sourceStore.ForEach((ulong modifierId) =>
        {
            if (found != 0) return;
            if (sourceStore.GetComponent(modifierId).HealerEntityId != healerId) return;
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != targetId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            found = modifierId;
        });
        return found;
    }
}
