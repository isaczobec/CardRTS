using System;
using System.Collections.Generic;

// A heavier, slower melee troop — see BasicMeleeTroopCard for the rebalance baseline this
// is scaled from. Values not explicitly specified in the design ask (shop gold cost, exact
// armor/health/speed deltas) are judgment calls consistent with that baseline; see each
// constant's comment.
public class IronKnightCard : SpawnAtPointCard
{
    private const int Damage = 56;
    // Slightly higher than BasicMeleeTroopCard.MaxHealth (250).
    private const int MaxHealth = 300;
    // Somewhat slower than BasicMeleeTroopCard.Speed (5).
    private const int Speed = 40;
    private const int Range = 5;
    // Higher than BasicMeleeTroopCard.Armor (20).
    private const int Armor = 40;
    // Half attack speed = double BasicMeleeTroopCard.AttackSpeedMilliseconds (333).
    private const float AttackSpeedMilliseconds = 790f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 24f;
    private const float AttackRangeMultiplier = 2.5f;
    // Shorter than BasicMeleeTroopCard's 3x — a proportionally quicker recovery relative to
    // its doubled windup, so its swing doesn't feel even more sluggish on top of the slower
    // attack speed.
    private const float CooldownMultiplier = 1.8f;

    // Takes 35% reduced damage on every 3rd hit it takes — see
    // PeriodicDamageReductionComponent/System.
    private const int PeriodicReductionHitInterval = 3;
    private const float PeriodicReductionRatio = 0.35f;

    private const float MaxDistanceFromBuilding = 20f;

    // Cooldown for the troop's test ability (see AbilityManager.MeleeStrikeAbilityId).
    private const float MeleeStrikeCooldownSeconds = 4f;

    // Pricier than BasicMeleeTroopCard (100) to match its higher power level.
    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.IronKnight;
    public override string Title => "Iron Knight";
    public override string ImageName => "IronKnight";
    public override string Description => "A heavily armored knight that hits hard and slow.";
    public override string IndicatorPrefabName => "IronKnight";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 150,
            Stone = 45
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.IronKnight, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // Indefinite modifier (TicksRemaining = int.MaxValue, no ActivatableComponent —
            // see DamageBoostUpgrade for the same shape) rather than a component directly on
            // the troop, so it composes correctly with any other modifier of this kind.
            (e, id) =>
            {
                EntityHandle modifier = e.CreateEntity();
                e.AddComponent(modifier.Id, new ModifierComponent
                {
                    TargetEntityId = id,
                    TicksRemaining = int.MaxValue,
                });
                e.AddComponent(modifier.Id, new PeriodicDamageReductionComponent
                {
                    HitInterval    = PeriodicReductionHitInterval,
                    ReductionRatio = PeriodicReductionRatio,
                });
            },
        });
    }
}
