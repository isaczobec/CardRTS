using System;
using System.Collections.Generic;

// Ranged troop modeled off IceManCard's own shape: a single seeking/homing pool
// (BasicRangedAISystem auto-attack only). Its projectiles carry a ProjectileOnHitComponent
// (EffectType = Burn), so every hit also applies/refreshes a stacking Scorched debuff
// (ModifierComponent + StackingBurnDebuffComponent + DamageOverTimeComponent, ModifierID.
// Scorched — see ProjectileOnHitSystem.ApplyBurn/ApplyScorch) to the target AND every other
// enemy troop within a small splash radius of the impact, alongside the normal
// DamageRequest. No ability equipped yet.
public class FireManCard : SpawnAtPointCard
{
    // See BasicMeleeTroopCard for the rebalance baseline this is scaled from — mirrors
    // IceManCard's own current values, except Damage (see BurnDamagePerStackRatio's own
    // comment for why it's deliberately lower).
    private const int MaxHealth = 200;
    private const int Speed = 45;
    private const int Range = 10;
    private const int Armor = 20;
    private const int Damage = 10;
    private const float AttackSpeedMilliseconds = 300f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // 4x — explicit design ask.
    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 20f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 32;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec

    // Scorched debuff applied/refreshed on every hit (see ProjectileOnHitSystem.ApplyBurn/
    // ApplyScorch) — judgment calls, easy to retune.
    private const float ScorchDurationSeconds = 10f;
    private const float ScorchProcPeriodSeconds = 1f;
    // Base damage/proc at 0 stacks (a fresh, unstacked application).
    private const float ScorchBaseDamagePerProc = 2f;
    // Multiplied by this troop's own Damage stat and added per stack — at Damage = 10 above,
    // this nets out to a tidy +2 damage/proc per stack.
    private const float ScorchDamagePerStackRatio = 0.2f;
    // Stacks (beyond the first, unstacked hit) a target can accumulate from consecutive
    // hits before capping out — see StackingBurnDebuffComponent.
    private const int ScorchMaxStacks = 5;
    // How far (as a multiple of this troop's own Range stat) the burn splashes from the
    // impact point onto other nearby enemies.
    private const float ScorchSplashRangeMultiplier = 0.3f;

    private const float MaxDistanceFromBuilding = 20f;

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.FireMan;
    public override string Title => "Fire Man";
    public override string ImageName => "FireMan";
    public override string Description => "A ranged troop whose homing shots scorch enemies, dealing stacking damage over time to everything near the impact.";
    public override string IndicatorPrefabName => "FireMan";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 100,
            Wood = 40,
            Gems = 10,
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.FireMan, stats, new List<Action<ECS, ulong>>
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
                    RenderableType.FireProjectile,
                    new ProjectileOnHitComponent
                    {
                        EffectType               = ProjectileOnHitEffectType.Burn,
                        DurationSeconds          = ScorchDurationSeconds,
                        BurnBaseDamagePerProc    = ScorchBaseDamagePerProc,
                        BurnDamagePerStackRatio  = ScorchDamagePerStackRatio,
                        BurnMaxStacks            = ScorchMaxStacks,
                        BurnProcPeriodTicks      = TickManager.SecondsToTicks(ScorchProcPeriodSeconds),
                        BurnSplashRangeMultiplier = ScorchSplashRangeMultiplier,
                    });
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
        });
    }
}
