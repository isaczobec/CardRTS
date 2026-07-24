using System;
using System.Collections.Generic;

// A large rock golem — slower and tankier than IronKnightCard (the baseline this is scaled
// from), with a permanent BuildingDamageBonusComponent modifier (see
// BuildingDamageBonusSystem) for its 200% increased damage against buildings, and a
// windup-based push ability (see AbilityManager.GroundSlamAbilityId/DisplacementSystem).
public class StoneConstructCard : SpawnAtPointCard
{
    // Explicit design ask.
    private const int Damage = 30;
    // More than IronKnightCard.MaxHealth (300).
    private const int MaxHealth = 450;
    // Slower than IronKnightCard.Speed (40).
    private const int Speed = 25;
    private const int Range = 5;
    // Higher than IronKnightCard.Armor (40) — a large rock golem is even tankier.
    private const int Armor = 55;
    // Slower than IronKnightCard.AttackSpeedMilliseconds (790).
    private const float AttackSpeedMilliseconds = 1100f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // Unchanged from IronKnightCard.
    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 24f;
    private const float AttackRangeMultiplier = 2.5f;
    private const float CooldownMultiplier = 1.8f;

    // Explicit design ask: 200% increased damage vs. buildings (see
    // BuildingDamageBonusComponent/System — request.Amount * (1 + BonusRatio), so 2.0 here
    // means 3x total).
    private const float BuildingDamageBonusRatio = 2.0f;

    // Cooldown for the troop's push ability (see AbilityManager.GroundSlamAbilityId).
    private const float GroundSlamCooldownSeconds = 8f;

    private const float MaxDistanceFromBuilding = 20f;

    // Pricier than IronKnightCard (100) to match its higher power level.
    public override int ShopGoldCost => 150;

    public override CardType Type => CardType.StoneConstruct;
    public override string Title => "Stone Construct";
    public override string ImageName => "StoneConstruct";
    public override string Description => "A slow, hulking rock golem that deals massive damage to buildings and can slam nearby troops away.";
    public override string IndicatorPrefabName => "StoneConstruct";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 100,
            Stone = 150
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.StoneConstruct, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // Indefinite modifier (TicksRemaining = int.MaxValue, no ActivatableComponent —
            // see IronKnightCard's PeriodicDamageReductionComponent for the same shape)
            // rather than a component directly on the troop, so it composes correctly with
            // any other modifier of this kind.
            (e, id) =>
            {
                EntityHandle modifier = e.CreateEntity();
                e.AddComponent(modifier.Id, new ModifierComponent
                {
                    TargetEntityId = id,
                    TicksRemaining = int.MaxValue,
                });
                e.AddComponent(modifier.Id, new BuildingDamageBonusComponent
                {
                    BonusRatio = BuildingDamageBonusRatio,
                });
            },
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id = AbilityManager.GroundSlamAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(GroundSlamCooldownSeconds),
            }),
        });
    }
}
