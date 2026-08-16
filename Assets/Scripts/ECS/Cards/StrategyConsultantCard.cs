using System;
using System.Collections.Generic;

// A ConstructionWorkerCard variant, further specialized into a pure support role: less HP
// and Speed than Construction Worker, and no building damage bonus, but two passive troop-
// wide effects instead:
//
// 1. A permanent aura self-buff — same shape as ShadowAngelCard/HealerGuardianCard's own
//    (a separate modifier entity targeting this troop, carrying PeriodicAreaEffectComponent,
//    granted once at spawn with infinite duration, periodically re-scanning to grant/refresh
//    a modifier on nearby friendly troops) — granting +20% attack speed (implemented as -20%
//    ratio bonus; lower AttackSpeed = faster, see StatModifierComponent's own convention).
//
// 2. An innate ResourceDropBoostComponent (see that class/ResourceDropBoostSystem): any
//    positional resource drop within range of this troop is boosted 30%.
public class StrategyConsultantCard : SpawnAtPointCard
{
    // Less than ConstructionWorkerCard's own MaxHealth (250) / Speed (50) — explicit design
    // ask.
    private const int MaxHealth = 140;
    private const int Speed = 37;

    // Unchanged from ConstructionWorkerCard/BasicMeleeTroopCard.
    private const int Range = 3;
    private const int Armor = 20;
    private const int Damage = 15;
    private const float AttackSpeedMilliseconds = 333f;
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 96f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    // Attack-speed aura — mirrors HealerGuardianCard's own HealRadiusMultiplier/
    // HealScanPeriodSeconds/GrantedHealDurationSeconds shape.
    // +20% attack speed — negative ratio since lower AttackSpeed = faster (see
    // StatModifierComponent's own convention, same as AttackSpeedBoostUpgrade).
    private const float AttackSpeedRatioBonus = -0.2f;
    private const float AuraScanPeriodSeconds = 1f;
    private const float AuraRadiusMultiplier = 2.5f;
    private const float GrantedBuffDurationSeconds = 4f;

    // Resource-drop boost — see ResourceDropBoostComponent/ResourceDropBoostSystem.
    private const float ResourceDropRangeMultiplier = 2.5f;
    private const float ResourceDropBoostRatio = 0.3f;

    // More expensive than ConstructionWorkerCard — explicit design ask.
    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.StrategyConsultant;
    public override string Title => "Strategy Consultant";
    public override string ImageName => "StrategyConsultant";
    public override string Description => "An aura grants nearby friendly troops 20% attack speed, and boosts nearby resource drops by 30%.";
    public override string IndicatorPrefabName => "StrategyConsultant";

    public override StatsComponent DefaultStats => BuildStats();

    // More expensive than ConstructionWorkerCard's own (Wood 160 / Stone 60 / Metal 20).
    public override ResourceCost Cost => new ResourceCost
        {
            Wood  = 160,
            Stone = 60,
            Metal = 50,
            Gems  = 5,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    static StrategyConsultantCard()
    {
        PeriodicAreaEffectSystem.RegisterEffect(AreaEffectType.AttackSpeedAura, ApplyAttackSpeedBuff);
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.StrategyConsultant, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // Permanent aura self-buff — separate modifier entity, infinite duration, same
            // shape as ShadowAngelCard's own aura (see its class doc comment for why a
            // separate entity rather than PeriodicAreaEffectComponent living directly on the
            // troop).
            (e, id) =>
            {
                EntityHandle auraModifier = e.CreateEntity();
                e.AddComponent(auraModifier.Id, new ModifierComponent
                {
                    TargetEntityId = id,
                    TicksRemaining = int.MaxValue,
                    ModifierID     = ModifierID.StrategyAura,
                });

                int scanPeriodTicks = TickManager.SecondsToTicks(AuraScanPeriodSeconds);
                e.AddComponent(auraModifier.Id, new PeriodicAreaEffectComponent
                {
                    RangeMultiplier    = AuraRadiusMultiplier,
                    PeriodTicks        = scanPeriodTicks,
                    TicksUntilNextProc = scanPeriodTicks,
                    Targets            = AreaTargetFlags.Friendly,
                    EffectType         = AreaEffectType.AttackSpeedAura,
                    Param0             = AttackSpeedRatioBonus,
                    Param2             = TickManager.SecondsToTicks(GrantedBuffDurationSeconds),
                });
                e.AddComponent(auraModifier.Id, new RenderableModifierComponent { Type = RenderableModifierType.StrategyAura });
            },
            // Innate trait, not a modifier — see ResourceDropBoostComponent's own doc
            // comment.
            (e, id) => e.AddComponent(id, new ResourceDropBoostComponent
            {
                RangeMultiplier = ResourceDropRangeMultiplier,
                BoostRatio      = ResourceDropBoostRatio,
            }),
        });
    }

    // ── Attack speed buff: registered against AreaEffectType.AttackSpeedAura ────────────────

    private static void ApplyAttackSpeedBuff(ECS ecs, ulong consultantId, ulong targetId, PeriodicAreaEffectComponent effect)
    {
        float attackSpeedRatioBonus = effect.Param0;
        int grantedDurationTicks = effect.Param2;

        ulong existingId = FindActiveBuffModifierId(ecs, consultantId, targetId);
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
            ModifierID     = ModifierID.AttackSpeedAura,
        });
        ecs.AddComponent(modifierEntity.Id, new StatModifierComponent
        {
            AttackSpeedRatioBonus = attackSpeedRatioBonus,
        });
        ecs.AddComponent(modifierEntity.Id, new StatAuraSourceComponent { SourceEntityId = consultantId });
        ecs.AddComponent(modifierEntity.Id, new RenderableModifierComponent { Type = RenderableModifierType.AttackSpeedAura });
    }

    // Scoped by SourceEntityId (not just target) — this is what lets a DIFFERENT Strategy
    // Consultant's own buff on the same target stack alongside this one instead of being
    // refreshed/overwritten by it. Mirrors ShadowAngelCard's own FindActiveShieldModifierId/
    // HealerGuardianCard's own FindActiveHealModifierId.
    private static ulong FindActiveBuffModifierId(ECS ecs, ulong consultantId, ulong targetId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<StatAuraSourceComponent> sourceStore = ecs.GetComponentStore<StatAuraSourceComponent>();
        if (modifierStore == null || sourceStore == null) return 0;

        ulong found = 0;
        sourceStore.ForEach((ulong modifierId) =>
        {
            if (found != 0) return;
            if (sourceStore.GetComponent(modifierId).SourceEntityId != consultantId) return;
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != targetId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            found = modifierId;
        });
        return found;
    }
}
