using System;
using System.Collections.Generic;

public class BasicRangedTroopCard : SpawnAtPointCard
{
    // See BasicMeleeTroopCard for the rebalance baseline this is scaled from (3x health,
    // 2.8x damage vs. the old 100 HP / 10 damage numbers, then damage 1.2x again to
    // compensate for 20 armor under ArmorMitigationSystem).
    private const int MaxHealth = 230;
    private const int Speed = 52;
    private const int Range = 11;
    private const int Armor = 20;
    private const int Damage = 29;
    private const float AttackSpeedMilliseconds = 525f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 18f; // 6x (explicit design ask) from 3
    // Same neutral:enemy ratio as SkillshotRangedTroopCard's own tuning (explicit design ask)
    // — back down at the pre-6x-bump value, since under Guard mode ANY tracked enemy always
    // wins over ANY tracked neutral regardless of relative distance (see
    // BasicRangedAISystem.ResolveGuardTarget), so the same 6x bump that's good for finding
    // resources from far away also meant this troop would beeline past a much closer tree/
    // rock to go fight a distant enemy.
    private const float EnemyDetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 26f; // +30% (explicit design ask) from 20
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 64;
    private const int ProjectileSpeedMilliTilesPerSecond = 15000; // 15 tiles/sec

    private const float MaxDistanceFromBuilding = 20f;

    public override CardType Type => CardType.BasicRangedTroop;
    public override string Title => "Archer";
    public override string ImageName => "BasicRangedTroop";
    public override string Description => "A ranged troop that peppers enemies with arrows from a distance.";
    public override string IndicatorPrefabName => "BasicRangedTroop";

    public override StatsComponent DefaultStats => BuildStats();
    // Metal folded into Wood/Stone (explicit design ask — metal/soulstones no longer used
    // for costs), sum unchanged (150). Moderate wood lean — a ranged troop, but not
    // especially light for its class.
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 90,
            Stone = 60
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    public override int ShopGoldCost => 100; 

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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.BasicRanged, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicRangedAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                EnemyDetectionRangeMultiplier = EnemyDetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                WindDownMultiplier       = WindDownMultiplier,
            }),
            (e, id) =>
            {
                ulong firstProjectileId = ProjectilePool.CreatePool(e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond);
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
        });
    }
}
