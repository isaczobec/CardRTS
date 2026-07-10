using System;
using System.Collections.Generic;

public class BasicRangedTroopCard : Card
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

    public override CardType Type => CardType.BasicRangedTroop;

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        StatsComponent stats = new StatsComponent
        {
            MaxHealth   = MaxHealth,
            Speed       = Speed,
            Range       = Range,
            Armor       = Armor,
            Damage      = Damage,
            AttackSpeed = TickManager.MillisecondsToTicks(AttackSpeedMilliseconds),
        };

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
