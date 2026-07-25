using System;
using System.Collections.Generic;

// Ranged troop with TWO pools (see ProjectileOwnerComponent's linked-list doc comment): its
// primary pool (auto-attack, fired by BasicRangedAISystem via ProjectilePool.Fire — same
// homing shape as BasicRangedTroopCard) is seeking/homing, while its second pool (fired
// only by the skillshot ability, see AbilityManager.BuildSkillshotAbility) is
// skillshot-typed (straight-line, radius-hit, piercing — see SkillshotProjectileComponent).
// Also grants its own HealthComponent a HitboxImmunityTicksToGive, so the piercing ability
// shot doesn't restack damage on the same target every tick it overlaps it (the homing
// auto-attack shot has no such concern — single target, single impact).
public class SkillshotRangedTroopCard : SpawnAtPointCard
{
    // See BasicMeleeTroopCard for the rebalance baseline this is scaled from (3x health,
    // 2.8x damage vs. the old 100 HP / 8 damage numbers, then damage 1.2x again to
    // compensate for 20 armor under ArmorMitigationSystem).
    private const int MaxHealth = 150;
    // Matches BasicMeleeTroopCard's own Speed.
    private const int Speed = 50;
    // 0.4x the previous 40 (which was itself 2.5x the original 16) — nets out to the
    // original 16.
    private const int Range = 21;
    private const int Armor = 20;
    private const int Damage = 26;
    private const float AttackSpeedMilliseconds = 900f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // 4x — explicit design ask.
    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 20f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 32;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec

    // Second pool, used only by the skillshot ability (see AbilityManager.
    // BuildSkillshotAbility's ProjectileOwnerIndex = 1) — quicker and longer-range than the
    // troop's own default auto-attack pool above. Range must be kept in step with
    // AbilityManager.SkillshotAbilityRange, which drives the ability's indicator circle —
    // see that constant's own comment.
    private const int AbilityProjectilePoolSize = 8;
    private const int AbilityProjectileSpeedMilliTilesPerSecond = 32000; // 32 tiles/sec
    // 3x the original 28.
    private const int AbilityProjectileRange = 84;
    // 2.5x the original 1.
    private const float AbilityProjectileHitRadius = 2.5f;
    // 1.4x the troop's plain Damage stat — the ability's piercing shot hits harder than
    // the ordinary homing auto-attack.
    private const float AbilityDamageMultiplier = 1.4f;

    // How long a target this troop's projectiles hit stays hitbox-immune afterward —
    // without this, the piercing shot would deal damage every single tick it overlaps the
    // same target instead of once per pass through it.
    private const float HitboxImmunityMilliseconds = 500f;

    // Cooldown lengths for this troop's three test abilities (see AbilityManager) —
    // cooldown length lives on AbilityComponent rather than on Ability itself, so
    // different troops could equip the same ability with different cooldowns.
    private const float RingOfProjectilesCooldownSeconds = 5f;
    private const float AoeSpellCloneCooldownSeconds = 8f;
    // Minimum time between individual casts, even with charges banked ("2 second cooldown
    // between charges").
    private const float SkillshotAbilityCooldownSeconds = 2f;
    // 3 shots can be banked and fired in quick succession (each still gated by the
    // cooldown above); each spent charge takes SkillshotChargeCooldownSeconds to
    // regenerate (see AbilityChargeSystem).
    private const int SkillshotMaxCharges = 3;
    private const float SkillshotChargeCooldownSeconds = 25f;

    public override int ShopGoldCost => 100;

    private const float MaxDistanceFromBuilding = 20f;

    public override CardType Type => CardType.SkillshotRangedTroop;
    public override string Title => "Ranger";
    public override string ImageName => "SkillshotRangedTroop";
    public override string Description => "A ranged troop with a homing auto-attack, plus an ability that fires a piercing shot straight ahead, hitting everything in its path.";
    public override string IndicatorPrefabName => "Ranger";

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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.Ranger, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicRangedAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                WindDownMultiplier       = WindDownMultiplier,
            }),
            // Primary pool — the troop's ordinary homing auto-attack (BasicRangedAISystem).
            (e, id) =>
            {
                ulong firstProjectileId = ProjectilePool.CreatePool(e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond);
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
            // Second, faster/longer-range pool used only by the skillshot ability instead
            // of this troop's own default auto-attack pool above — a separate entity so it
            // can carry its own Range stat (read by ProjectilePool.FireInDirection's
            // AimSkillshot) independently of this troop's own Range (used for its ranged
            // auto-attack's targeting/attack-range checks instead). Projectiles are still
            // created with THIS troop (id) as their owner, so kill/damage attribution stays
            // correct (see ProjectileBaseComponent.OwnerEntityId) — only the pool
            // bookkeeping (ProjectileOwnerComponent) lives on the separate entity, linked
            // from the troop's own primary pool via NextProjectileOwnerId.
            (e, id) =>
            {
                EntityHandle abilityPoolOwner = e.CreateEntity();
                e.AddComponent(abilityPoolOwner.Id, new StatsComponent
                {
                    MaxHealth   = StatsComponent.STAT_NA,
                    Speed       = StatsComponent.STAT_NA,
                    Range       = AbilityProjectileRange,
                    Armor       = StatsComponent.STAT_NA,
                    Damage      = StatsComponent.STAT_NA,
                    AttackSpeed = StatsComponent.STAT_NA,
                    SpellResist = StatsComponent.STAT_NA,
                });

                ulong firstAbilityProjectileId = ProjectilePool.CreateSkillshotPool(
                    e, id, AbilityProjectilePoolSize, AbilityProjectileSpeedMilliTilesPerSecond, AbilityProjectileHitRadius,
                    RenderableType.FastSkillshotProjectile, AbilityDamageMultiplier);
                e.AddComponent(abilityPoolOwner.Id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = AbilityProjectilePoolSize,
                    NextProjectileId = firstAbilityProjectileId,
                });

                ref ProjectileOwnerComponent primaryOwner = ref e.GetComponentStore<ProjectileOwnerComponent>().GetComponent(id);
                primaryOwner.NextProjectileOwnerId = abilityPoolOwner.Id;
                e.Delta.MarkComponentDirty(id, typeof(ProjectileOwnerComponent));
            },
            (e, id) =>
            {
                ComponentStore<HealthComponent> healthStore = e.GetComponentStore<HealthComponent>();
                ref HealthComponent health = ref healthStore.GetComponent(id);
                health.HitboxImmunityTicksToGive = TickManager.MillisecondsToTicks(HitboxImmunityMilliseconds);
                e.Delta.MarkComponentDirty(id, typeof(HealthComponent));
            },
            // Test abilities (Q/W/E) — see AbilityManager. Slot 4 (R) is left empty (0).
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id = AbilityManager.SkillshotAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(SkillshotAbilityCooldownSeconds),
                Ability1MaxCharges = SkillshotMaxCharges,
                Ability1ChargesRemaining = SkillshotMaxCharges,
                Ability1ChargeCooldownTicks = TickManager.SecondsToTicks(SkillshotChargeCooldownSeconds),
            }),
        });
    }
}
