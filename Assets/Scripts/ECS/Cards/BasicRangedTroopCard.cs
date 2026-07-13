using System;
using System.Collections.Generic;

public class BasicRangedTroopCard : SpawnAtPointCard
{
    private const int MaxHealth = 100;
    private const int Speed = 10;
    private const int Range = 60;
    private const int Armor = 0;
    private const int Damage = 10;
    private const float AttackSpeedMilliseconds = 800f;

    private const float DetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 5f;
    private const float AttackRangeMultiplier = 1.5f;

    private const int ProjectilePoolSize = 64;
    private const int ProjectileSpeedMilliTilesPerSecond = 15000; // 15 tiles/sec

    private const int GoldCost = 4;

    private const float MaxDistanceFromBuilding = 20f;

    public override CardType Type => CardType.BasicRangedTroop;
    public override string Title => "Ranged Troop";
    public override string ImageName => "BasicRangedTroop";
    public override string Description => "A ranged troop that peppers enemies with arrows from a distance.";
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

        TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.BasicRanged, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicRangedAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
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
