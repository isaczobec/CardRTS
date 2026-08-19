using System;
using System.Collections.Generic;

public class BasicMeleeTroopCard : SpawnAtPointCard
{
    // Balance baseline: a basic melee troop still kills another basic melee troop (300 HP)
    // in ~11 hits (ceil(300/28) = 11 before armor; see ArmorMitigationSystem's formula —
    // 34 raw damage vs. 20 armor mitigates to round(34 * 100/120) = 28, reproducing that
    // same 28-effective-damage-per-hit exactly). Every other troop/building/resource-node
    // health and damage value in this rebalance is scaled proportionally off this pair (3x
    // health, 2.8x damage vs. the old 100 HP / 10 damage baseline, then damage further
    // scaled 1.2x to compensate for 20 armor).
    private const int MaxHealth = 250;
    private const int Speed = 50;
    private const int Range = 5;
    private const int Armor = 20;
    private const int Damage = 34;
    private const float AttackSpeedMilliseconds = 333f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 72f; // 6x (explicit design ask) from 12
    // Same neutral:enemy ratio as SkillshotRangedTroopCard's own tuning (explicit design ask)
    // — back down at the pre-6x-bump value, since under Guard mode ANY tracked enemy always
    // wins over ANY tracked neutral regardless of relative distance (see
    // BasicMeleeAISystem.ResolveGuardTarget), so the same 6x bump that's good for finding
    // resources from far away also meant this troop would beeline past a much closer tree/
    // rock to go fight a distant enemy.
    private const float EnemyDetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 124.8f; // +30% (explicit design ask) from 96
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;


    private const float MaxDistanceFromBuilding = 20f;

    // Cooldown for the troop's test ability (see AbilityManager.MeleeStrikeAbilityId).
    private const float MeleeStrikeCooldownSeconds = 4f;

    public override int ShopGoldCost => 100; 

    public override CardType Type => CardType.BasicMeleeTroop;
    public override string Title => "Warrior";
    public override string ImageName => "BasicMeleeTroop";
    public override string Description => "A sturdy melee troop that charges the nearest enemy.";
    public override string IndicatorPrefabName => "BasicTroop";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost 
        { 
            Wood = 120, 
            Stone = 30 
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.BasicMelee, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                EnemyDetectionRangeMultiplier = EnemyDetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
        });
    }
}
