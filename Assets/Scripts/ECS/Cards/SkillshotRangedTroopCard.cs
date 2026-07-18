using System;
using System.Collections.Generic;

// Same "ranged troop with a projectile pool" shape as BasicRangedTroopCard, but its pool
// is skillshot-typed (straight-line, radius-hit, piercing — see SkillshotProjectileComponent)
// instead of homing. No AI system changes needed at all: BasicRangedAISystem already just
// calls ProjectilePool.Fire, which auto-detects which kind of projectile a troop's pool
// holds and aims/resets it accordingly (see ProjectilePool.Fire) — this card only differs
// from BasicRangedTroopCard in which CreatePool variant it calls and in granting its
// troop's own HealthComponent a HitboxImmunityTicksToGive, so its piercing shot doesn't
// restack damage on the same target every tick it overlaps it.
public class SkillshotRangedTroopCard : SpawnAtPointCard
{
    // See BasicMeleeTroopCard for the rebalance baseline this is scaled from (3x health,
    // 2.8x damage vs. the old 100 HP / 8 damage numbers, then damage 1.2x again to
    // compensate for 20 armor under ArmorMitigationSystem).
    private const int MaxHealth = 300;
    private const int Speed = 6;
    private const int Range = 16;
    private const int Armor = 20;
    private const int Damage = 26;
    private const float AttackSpeedMilliseconds = 900f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 5f;
    private const float AttackRangeMultiplier = 1.5f;

    private const int ProjectilePoolSize = 32;
    private const int ProjectileSpeedMilliTilesPerSecond = 20000; // 20 tiles/sec
    private const float ProjectileHitRadius = 1f;

    // How long a target this troop's projectiles hit stays hitbox-immune afterward —
    // without this, the piercing shot would deal damage every single tick it overlaps the
    // same target instead of once per pass through it.
    private const float HitboxImmunityMilliseconds = 500f;

    // Cooldown lengths for this troop's three test abilities (see AbilityManager) —
    // cooldown length lives on AbilityComponent rather than on Ability itself, so
    // different troops could equip the same ability with different cooldowns.
    private const float RingOfProjectilesCooldownSeconds = 5f;
    private const float AoeSpellCloneCooldownSeconds = 8f;
    private const float SkillshotAbilityCooldownSeconds = 3f;

    private const int GoldCost = 4;

    public override int ShopGoldCost => 10; 

    private const float MaxDistanceFromBuilding = 20f;

    public override CardType Type => CardType.SkillshotRangedTroop;
    public override string Title => "Skillshot Troop";
    public override string ImageName => "SkillshotRangedTroop";
    public override string Description => "A ranged troop that fires a piercing shot straight ahead, hitting everything in its path.";
    public override string IndicatorPrefabName => "SkillshotRangedTroop";

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
        SpellResist = SpellResist,
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
                ulong firstProjectileId = ProjectilePool.CreateSkillshotPool(e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond, ProjectileHitRadius);
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
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
                Ability1Id = AbilityManager.RingOfProjectilesAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(RingOfProjectilesCooldownSeconds),
                Ability2Id = AbilityManager.AoeSpellCloneAbilityId,
                Ability2CooldownTicks = TickManager.SecondsToTicks(AoeSpellCloneCooldownSeconds),
                Ability3Id = AbilityManager.SkillshotAbilityId,
                Ability3CooldownTicks = TickManager.SecondsToTicks(SkillshotAbilityCooldownSeconds),
            }),
        });
    }
}
