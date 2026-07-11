using System;
using System.Collections.Generic;

public class BasicMeleeTroopCard : Card
{
    private const int MaxHealth = 100;
    private const int Speed = 10;
    private const int Range = 5;
    private const int Armor = 0;
    private const int Damage = 10;
    private const float AttackSpeedMilliseconds = 333f;

    private const float DetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 5f;
    private const float AttackRangeMultiplier = 1.5f;

    private const int GoldCost = 3;

    public override CardType Type => CardType.BasicMeleeTroop;
    public override string Title => "Melee Troop";
    public override string ImageName => "BasicMeleeTroop";
    public override string Description => "A sturdy melee troop that charges the nearest enemy.";
    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost { Gold = GoldCost };

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
        });
    }
}
