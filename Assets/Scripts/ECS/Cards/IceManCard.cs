using System;
using System.Collections.Generic;

// Ranged troop modeled off SkillshotRangedTroopCard's own baseline stats, but simpler: a
// single seeking/homing pool (BasicRangedAISystem auto-attack only, same shape as
// SkillshotRangedTroopCard's own primary pool) and no abilities at all to begin with. Its
// projectiles carry a ProjectileOnHitComponent (EffectType = Slow), so every hit also
// applies a Chilled modifier (ModifierComponent + StatModifierComponent, ModifierID.Chilled
// — see ProjectileOnHitSystem.ApplySlow) alongside the normal DamageRequest.
public class IceManCard : SpawnAtPointCard
{
    // See BasicMeleeTroopCard for the rebalance baseline this is scaled from — mirrors
    // SkillshotRangedTroopCard's own current values.
    private const int MaxHealth = 150;
    private const int Speed = 40;
    private const int Range = 16;
    private const int Armor = 20;
    private const int Damage = 26;
    private const float AttackSpeedMilliseconds = 900f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 5f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 32;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec

    // Chilled slow applied on every hit (see ProjectileOnHitSystem.ApplySlow) — judgment
    // calls, easy to retune.
    private const float SlowRatio = -0.3f;
    private const float SlowDurationSeconds = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.IceMan;
    public override string Title => "Ice Man";
    public override string ImageName => "IceMan";
    public override string Description => "A ranged troop whose homing shots chill enemies, slowing their movement.";
    public override string IndicatorPrefabName => "IceMan";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 120,
            Wood = 30
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.IceMan, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicRangedAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                WindDownMultiplier       = WindDownMultiplier,
            }),
            (e, id) =>
            {
                ulong firstProjectileId = ProjectilePool.CreatePool(
                    e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond,
                    RenderableType.IceProjectile,
                    new ProjectileOnHitComponent
                    {
                        EffectType      = ProjectileOnHitEffectType.Slow,
                        SlowRatio       = SlowRatio,
                        DurationSeconds = SlowDurationSeconds,
                    });
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
            // No abilities to begin with — no AbilityComponent granted at all (same as
            // BasicRangedTroopCard).
        });
    }
}
