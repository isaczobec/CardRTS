using System;
using System.Collections.Generic;

// Ranged support troop based on HealerGuardianCard — same homing auto-attack shape
// (BasicRangedAIComponent + a single ProjectilePool) and the exact same self-buff aura shape
// (a separate modifier entity targeting this troop, carrying PeriodicAreaEffectComponent,
// which periodically grants/refreshes a modifier on nearby friendly troops), but tankier
// (250 HP vs 90), faster-attacking, slightly quicker, and hits much harder (80 vs 5 damage)
// — explicit design ask.
//
// Unlike Healer Guardian's own aura (hit-triggered, expires after 12s), this one is granted
// once at spawn with infinite duration (ModifierComponent.TicksRemaining = int.MaxValue —
// ModifierSystem never counts that down, so it's never re-earned/refreshed) — explicit
// design ask, and consistent with "should not have a lifetime component" on the troop
// itself. Kept as its own separate modifier entity (ModifierID.ShadowAura,
// RenderableModifierType.ShadowAura) rather than PeriodicAreaEffectComponent living directly
// on the troop (DamageAuraComponent's own alternate mode) specifically so
// AreaEffectRadiusModifierRenderer — which already expects any PeriodicAreaEffectComponent
// it drives to sit on a ModifierComponent-carrying entity, not the target directly — can be
// reused to show the aura's radius, exactly like it already does for Healer Guardian's own
// aura.
//
// The aura grants/refreshes a ShadowAngelDamageShareComponent modifier (see
// ShadowAngelDamageShareSystem) on each nearby friendly troop found — while active, that
// modifier redirects 90% of any damage this Shadow Angel takes onto whichever troops
// currently hold it, split evenly across all of them.
public class ShadowAngelCard : SpawnAtPointCard
{
    // Explicit design ask — vs. Healer Guardian's own MaxHealth 90 / AttackSpeed 900ms /
    // Speed 36 / Damage 5.
    private const int MaxHealth = 150;
    private const float AttackSpeedMilliseconds = 600f;
    private const int Speed = 40;
    private const int Damage = 80;

    // Unchanged from HealerGuardianCard.
    private const int Armor = 20;
    private const int SpellResist = 0;
    private const int Range = 12;
    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 20f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 32;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec

    // Fraction of this troop's own incoming damage redirected onto whichever nearby friendly
    // troops currently hold its aura buff — explicit design ask.
    private const float DamageShareRatio = 0.9f;

    // How often the aura re-scans for nearby friendly troops to apply/refresh the damage-share
    // buff on — see PeriodicAreaEffectSystem. Mirrors HealerGuardianCard's own HealScanPeriodSeconds.
    private const float ShieldScanPeriodSeconds = 1f;

    // Multiple of this troop's own Range stat — mirrors HealerGuardianCard's own
    // HealRadiusMultiplier.
    private const float ShieldRadiusMultiplier = 2.5f;

    // How long an individual ally's own granted damage-share buff lasts before it needs
    // refreshing by another scan — kept longer than ShieldScanPeriodSeconds so a missed scan
    // or two doesn't let it lapse. Mirrors HealerGuardianCard's own GrantedHealDurationSeconds.
    private const float GrantedShieldDurationSeconds = 4f;

    private const float MaxDistanceFromBuilding = 20f;
    // A support troop should be deployable alongside the friendly troops it's meant to
    // shield, not only near a building — mirrors HealerGuardianCard's own reasoning.
    private const float MaxDistanceFromTroop = 15f;

    public override int ShopGoldCost => 180;

    public override CardType Type => CardType.ShadowAngel;
    public override string Title => "Shadow Angel";
    public override string ImageName => "ShadowAngel";
    public override string Description => "A tough ranged support troop with an aura that shares 90% of the damage it takes with nearby friendly troops.";
    public override string IndicatorPrefabName => "ShadowAngel";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 150,
            Wood = 40,
            Soulstones = 1,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;

    static ShadowAngelCard()
    {
        PeriodicAreaEffectSystem.RegisterEffect(AreaEffectType.ShadowShield, ApplyShadowShield);
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.ShadowAngel, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicRangedAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                WindDownMultiplier       = WindDownMultiplier,
            }),
            // Lets ShadowAngelDamageShareSystem cap this troop to redistributing damage at
            // most once per tick — see ShadowAngelComponent's own doc comment.
            (e, id) => e.AddComponent(id, new ShadowAngelComponent()),
            (e, id) =>
            {
                ulong firstProjectileId = ProjectilePool.CreatePool(e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond, RenderableType.ShadowAngelProjectile);
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
            // Permanent aura self-buff — a separate modifier entity targeting this troop, with
            // infinite duration (see the class doc comment for why a separate entity rather
            // than PeriodicAreaEffectComponent living directly on the troop).
            (e, id) =>
            {
                EntityHandle auraModifier = e.CreateEntity();
                e.AddComponent(auraModifier.Id, new ModifierComponent
                {
                    TargetEntityId = id,
                    TicksRemaining = int.MaxValue,
                    ModifierID     = ModifierID.ShadowAura,
                });

                int scanPeriodTicks = TickManager.SecondsToTicks(ShieldScanPeriodSeconds);
                e.AddComponent(auraModifier.Id, new PeriodicAreaEffectComponent
                {
                    RangeMultiplier    = ShieldRadiusMultiplier,
                    PeriodTicks        = scanPeriodTicks,
                    TicksUntilNextProc = scanPeriodTicks,
                    Targets            = AreaTargetFlags.Friendly,
                    EffectType         = AreaEffectType.ShadowShield,
                    Param0             = DamageShareRatio,
                    Param2             = TickManager.SecondsToTicks(GrantedShieldDurationSeconds),
                });
                e.AddComponent(auraModifier.Id, new RenderableModifierComponent { Type = RenderableModifierType.ShadowAura });
            },
        });
    }

    // ── Shadow shield effect: registered against AreaEffectType.ShadowShield ────────────────

    private static void ApplyShadowShield(ECS ecs, ulong angelId, ulong targetId, PeriodicAreaEffectComponent effect)
    {
        float shareRatio = effect.Param0;
        int grantedDurationTicks = effect.Param2;

        ulong existingId = FindActiveShieldModifierId(ecs, angelId, targetId);
        if (existingId != 0)
        {
            ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
            ref ModifierComponent modifier = ref modifierStore.GetComponent(existingId);
            modifier.TicksRemaining = grantedDurationTicks;
            ecs.Delta.MarkComponentDirty(existingId, typeof(ModifierComponent));
            return;
        }

        EntityHandle modifierEntity = ecs.CreateEntity();
        ecs.AddComponent(modifierEntity.Id, new ModifierComponent
        {
            TargetEntityId = targetId,
            TicksRemaining = grantedDurationTicks,
            ModifierID     = ModifierID.ShadowShield,
        });
        ecs.AddComponent(modifierEntity.Id, new ShadowAngelDamageShareComponent
        {
            SourceEntityId = angelId,
            ShareRatio     = shareRatio,
        });
        ecs.AddComponent(modifierEntity.Id, new RenderableModifierComponent { Type = RenderableModifierType.ShadowShield });
    }

    // Scoped by SourceEntityId (not just target) — this is what lets a DIFFERENT Shadow
    // Angel's own damage-share modifier on the same target stack alongside this one instead
    // of being refreshed/overwritten by it. Mirrors HealerGuardianCard's own
    // FindActiveHealModifierId.
    private static ulong FindActiveShieldModifierId(ECS ecs, ulong angelId, ulong targetId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<ShadowAngelDamageShareComponent> shareStore = ecs.GetComponentStore<ShadowAngelDamageShareComponent>();
        if (modifierStore == null || shareStore == null) return 0;

        ulong found = 0;
        shareStore.ForEach((ulong modifierId) =>
        {
            if (found != 0) return;
            if (shareStore.GetComponent(modifierId).SourceEntityId != angelId) return;
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != targetId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            found = modifierId;
        });
        return found;
    }
}
