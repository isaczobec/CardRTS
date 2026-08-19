using System;
using System.Collections.Generic;

// A BasicMeleeTroopCard ("the Warrior") variant — same baseline stats, just pricier and
// weaker in a straight fight (a little under half the Warrior's own Damage), but carries a
// permanent BuildingDamageBonusComponent modifier (+200% damage vs. buildings — see
// BuildingDamageBonusSystem) and an innate BuildingRefundAuraComponent: playing a friendly
// building card within RefundRangeMultiplier x this troop's own Range instantly refunds
// RefundRatio (rounded down) of that building's own resource cost back to its owner — see
// BuildingRefundHelper, called from SpawnAtPointCardPlaySystem right after any card's spawn.
public class ConstructionWorkerCard : SpawnAtPointCard
{
    // Unchanged from BasicMeleeTroopCard ("the Warrior").
    private const int MaxHealth = 160;
    private const int Speed = 40;
    private const int Range = 3;
    private const int Armor = 20;
    private const float AttackSpeedMilliseconds = 333f;
    private const int SpellResist = 0;

    // A little less than half of BasicMeleeTroopCard.Damage (34) — explicit design ask.
    private const int Damage = 15;

    // Unchanged from BasicMeleeTroopCard.
    private const float DetectionRangeMultiplier = 72f; // 6x (explicit design ask) from 12
    // Same neutral:enemy ratio as SkillshotRangedTroopCard's own tuning (explicit design ask)
    // — back down at the pre-6x-bump value; see BasicMeleeTroopCard's own identical comment.
    private const float EnemyDetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 124.8f; // +30% (explicit design ask) from 96
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    // +200% damage vs. buildings — BuildingDamageBonusSystem's formula is
    // Amount * (1 + BonusRatio), so 2.0 here means 3x total (200% MORE, not 200% of).
    private const float BuildingDamageBonusRatio = 1.6f;

    // Multiple of this troop's own Range stat — how close a friendly building card must be
    // played to trigger the refund (explicit design ask: "some multiple of the range").
    private const float RefundRangeMultiplier = 3f;
    private const float RefundRatio = 0.3f;

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.ConstructionWorker;
    public override string Title => "Construction Worker";
    public override string ImageName => "ConstructionWorker";
    public override string Description => "Deals 200% bonus damage to buildings. Playing a friendly building card nearby instantly refunds 30% of its resource cost.";
    public override string IndicatorPrefabName => "ConstructionWorker";

    public override StatsComponent DefaultStats => BuildStats();

    // More expensive than BasicMeleeTroopCard (Wood 120 / Stone 30) — explicit design ask.
    // Metal folded into Wood/Stone, sum unchanged (265).
    public override ResourceCost Cost => new ResourceCost
        {
            Wood  = 185,
            Stone = 80,
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.ConstructionWorker, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                EnemyDetectionRangeMultiplier = EnemyDetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // Indefinite modifier (TicksRemaining = int.MaxValue, no ActivatableComponent —
            // see GoblinSnatcherCard/StoneConstructCard for the same shape) rather than a
            // component directly on the troop, so it composes correctly with any other
            // modifier of this kind.
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
            // Innate trait, not a modifier — see BuildingRefundAuraComponent's own doc
            // comment.
            (e, id) => e.AddComponent(id, new BuildingRefundAuraComponent
            {
                RangeMultiplier = RefundRangeMultiplier,
                RefundRatio     = RefundRatio,
            }),
        });
    }
}
