using System;
using System.Collections.Generic;

// Ranged troop modeled directly off IceManCard's own shape — same single seeking/homing
// pool (BasicRangedAISystem auto-attack only) with the same on-hit Chilled slow (including
// its AOE splash — see ProjectileOnHitSystem.ApplySlow), just at 60% of IceManCard's own
// MaxHealth/Damage (explicit design ask). The one real departure: instead of equipping Ice
// Nova, this equips AbilityManager's Gravity Well ability — a point-targeted AOE telegraph
// (mirrors AoeRootCard/MassiveSleepingDraughtCard's own telegraph-then-delayed-resolve
// shape) that collapses 4 seconds after being cast, pulling every enemy caught inside toward
// its center. See AbilityManager.BuildGravityWellAbility/ResolveGravityWell for the ability
// itself.
public class PurpleWizardCard : SpawnAtPointCard
{
    // 60% of IceManCard's own MaxHealth (220) — explicit design ask.
    private const int MaxHealth = 132;
    // 60% of IceManCard's own Damage (24), rounded — explicit design ask.
    private const int Damage = 14;

    // Unchanged from IceManCard.
    private const int Speed = 40;
    private const int Range = 13;
    private const int Armor = 20;
    private const float AttackSpeedMilliseconds = 450f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 18f; // 6x (explicit design ask) from 3
    // Same neutral:enemy ratio as SkillshotRangedTroopCard's own tuning (explicit design ask)
    // — back down at the pre-6x-bump value; see BasicRangedTroopCard's own identical comment.
    private const float EnemyDetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 26f; // +30% (explicit design ask) from 20
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 32;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec

    // Chilled slow applied on every hit (see ProjectileOnHitSystem.ApplySlow) — unchanged
    // from IceManCard, including its AOE splash.
    private const float SlowRatio = -0.35f;
    private const float SlowDurationSeconds = 6f;
    private const float SlowSplashRangeMultiplier = 0.48f;

    // Cooldown length lives on AbilityComponent rather than on Ability itself, so different
    // troops could equip the same ability with different cooldowns. Slightly longer than
    // IceManCard's own IceNovaCooldownSeconds (13s) — Gravity Well is a stronger CC/utility
    // effect than Ice Nova's damage-only nova.
    private const float GravityWellCooldownSeconds = 16f;

    private const float MaxDistanceFromBuilding = 20f;

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.PurpleWizard;
    public override string Title => "Purple Wizard";
    public override string ImageName => "PurpleWizard";
    public override string Description => "A ranged troop whose homing shots chill enemies. Can conjure a gravity well that pulls nearby enemies together.";
    public override string IndicatorPrefabName => "PurpleWizard";

    public override StatsComponent DefaultStats => BuildStats();
    // Metal folded into Wood/Stone, plus Gems=10 also folded in (explicit design ask —
    // troops/buildings no longer cost Gems at all) — same split as IceManCard, which this
    // is based off of.
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 110,
            Stone = 80,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override int[] GrantedAbilityIds => new[] { AbilityManager.GravityWellAbilityId };

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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.PurpleWizard, stats, new List<Action<ECS, ulong>>
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
                ulong firstProjectileId = ProjectilePool.CreatePool(
                    e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond,
                    RenderableType.IceProjectile,
                    new ProjectileOnHitComponent
                    {
                        EffectType                = ProjectileOnHitEffectType.Slow,
                        SlowRatio                 = SlowRatio,
                        DurationSeconds           = SlowDurationSeconds,
                        SlowSplashRangeMultiplier = SlowSplashRangeMultiplier,
                    });
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id = AbilityManager.GravityWellAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(GravityWellCooldownSeconds),
            }),
        });
    }
}
