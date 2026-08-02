using System;
using System.Collections.Generic;

// Fast, fragile raider scaled off BasicMeleeTroopCard's own baseline stats (slightly more
// than half its health, half its damage, 25% higher movement speed, 15% higher attack
// speed — see each stat constant's own comment for the exact derivation), plus a permanent
// BuildingDamageBonusComponent modifier (+20% damage dealt to buildings — see
// BuildingDamageBonusSystem) that rewards using it to raid rather than to fight troops.
public class GoblinSnatcherCard : SpawnAtPointCard
{
    // Slightly more than half of BasicMeleeTroopCard.MaxHealth (250) — a raider, not a
    // frontline brawler.
    private const int MaxHealth = 140;
    // 25% higher than BasicMeleeTroopCard.Speed (50), rounded (50 * 1.25 = 62.5).
    private const int Speed = 58;
    // Unchanged from BasicMeleeTroopCard.
    private const int Range = 3;
    private const int Armor = 20;
    // Exactly half of BasicMeleeTroopCard.Damage (34).
    private const int Damage = 14;
    // 15% higher attack speed than BasicMeleeTroopCard's 333ms — "higher attack speed"
    // means a shorter windup, so this divides rather than multiplies (mirrors
    // IronKnightCard's own "half attack speed = double AttackSpeedMilliseconds" convention,
    // just in the opposite direction): 333 / 1.15 = 289.6.
    private const float AttackSpeedMilliseconds = 290f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // Unchanged from BasicMeleeTroopCard.
    // 4x — explicit design ask.
    private const float DetectionRangeMultiplier = 48f;
    private const float ChaseRangeMultiplier = 96f;
    private const float AttackRangeMultiplier = 4f;
    private const float CooldownMultiplier = 2f;

    // Permanent damage bonus vs. buildings (see BuildingDamageBonusComponent/System).
    private const float BuildingDamageBonusRatio = 0.3f;

    private const float MaxDistanceFromBuilding = 20f;

    // Cheaper than BasicMeleeTroopCard (100) to match its lower power level as a
    // troop-vs-troop fighter.
    public override int ShopGoldCost => 60;

    public override CardType Type => CardType.GoblinSnatcher;
    public override string Title => "Goblin Snatcher";
    public override string ImageName => "GoblinSnatcher";
    public override string Description => $"A fast, fragile raider that deals {BuildingDamageBonusRatio*100:0f}% more damage to buildings.";
    public override string IndicatorPrefabName => "GoblinSnatcher";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 100,
            Stone = 20
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.GoblinSnatcher, stats, new List<Action<ECS, ulong>>
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
        });
    }
}
