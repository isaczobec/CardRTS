using System;
using System.Collections.Generic;

public class BasicMeleeTroopCard : SpawnAtPointCard
{
    private const int MaxHealth = 100;
    private const int Speed = 5;
    private const int Range = 5;
    private const int Armor = 0;
    private const int Damage = 10;
    private const float AttackSpeedMilliseconds = 333f;

    private const float DetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 5f;
    private const float AttackRangeMultiplier = 1.5f;

    private const int GoldCost = 3;

    private const float MaxDistanceFromBuilding = 20f;

    // Cooldown for the troop's test ability (see AbilityManager.MeleeStrikeAbilityId).
    private const float MeleeStrikeCooldownSeconds = 4f;

    public override int ShopGoldCost => 10; 

    public override CardType Type => CardType.BasicMeleeTroop;
    public override string Title => "Melee Troop";
    public override string ImageName => "BasicMeleeTroop";
    public override string Description => "A sturdy melee troop that charges the nearest enemy.";
    public override string IndicatorPrefabName => "BasicTroop";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost { Gold = GoldCost };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = Speed,
        Range       = Range,
        Armor       = Armor,
        Damage      = Damage,
        AttackSpeed = TickManager.MillisecondsToTicks(AttackSpeedMilliseconds),
    };

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        StatsComponent stats = BuildStats();

        TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.BasicMelee, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
            }),
            // Test ability (Q) — see AbilityManager. Slots 2-4 (W/E/R) are left empty (0).
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id = AbilityManager.MeleeStrikeAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(MeleeStrikeCooldownSeconds),
            }),
        });
    }
}
