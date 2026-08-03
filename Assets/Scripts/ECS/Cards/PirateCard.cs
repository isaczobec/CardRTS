using System;
using System.Collections.Generic;

// A BasicMeleeTroopCard ("Warrior") variant — same baseline stats, just a little weaker in
// Damage and Speed — equipped with AbilityManager.HookAbilityId: a skillshot ability fired
// EXACTLY the way SkillshotRangedTroopCard's own ability is (ActionWindupComponent +
// FireProjectileOnExpireComponent, see AbilityManager.BuildHookAbility), from a dedicated
// pool that's this troop's only pool (a melee troop has no ordinary ranged auto-attack pool
// to share it with — see ProjectileOwnerIndex = 0 on the ability).
//
// The hook's own SkillshotProjectileComponent has StopOnFirstHit = true (unlike every other
// skillshot pool in this codebase, which pierces) — a grappling hook grabs ONE troop and
// stops, rather than piercing through everyone in its path — and its
// ProjectileOnHitComponent (EffectType.Hook — see ProjectileOnHitSystem.ApplyHook) yanks
// whatever enemy troop it hits to just behind THIS Pirate's own current position at the
// moment of the hit, via DisplacementSystem.
public class PirateCard : SpawnAtPointCard
{
    // Unchanged from BasicMeleeTroopCard — see its own comment for the balance baseline
    // these are scaled from.
    private const int MaxHealth = 250;
    // A little worse than BasicMeleeTroopCard.Speed (50) — explicit design ask.
    private const int Speed = 44;
    private const int Range = 5;
    private const int Armor = 20;
    // A little worse than BasicMeleeTroopCard.Damage (34) — explicit design ask.
    private const int Damage = 28;
    private const float AttackSpeedMilliseconds = 333f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // Unchanged from BasicMeleeTroopCard.
    private const float DetectionRangeMultiplier = 48f;
    private const float ChaseRangeMultiplier = 96f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    // Hook ability's own (only) pool — see AbilityManager.HookAbilityId/BuildHookAbility.
    private const int HookProjectilePoolSize = 8;
    private const int HookProjectileSpeedMilliTilesPerSecond = 35000; // 20 tiles/sec
    private const float HookHitRadius = 1.5f;
    // 1 = the hook hits for exactly this troop's own plain Damage stat.
    private const float HookDamageMultiplier = 1f;

    // Hook on-hit pull — see ProjectileOnHitComponent/ProjectileOnHitSystem.ApplyHook.
    private const float HookPullBehindOffset = 2f;
    private const float HookPullDurationSeconds = 1.0f;

    private const float HookAbilityCooldownSeconds = 20f;

    public override int ShopGoldCost => 110;

    public override CardType Type => CardType.Pirate;
    public override string Title => "Pirate";
    public override string ImageName => "Pirate";
    public override string Description => "A melee troop with a hook that yanks a hit enemy troop to just behind it.";
    public override string IndicatorPrefabName => "Pirate";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 120,
            Stone = 30
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override int[] GrantedAbilityIds => new[] { AbilityManager.HookAbilityId };

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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.Pirate, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // This troop's only pool — the Hook ability's (index 0, see
            // AbilityManager.BuildHookAbility.ProjectileOwnerIndex). Melee troops have no
            // ordinary ranged auto-attack pool to share it with.
            (e, id) =>
            {
                ulong firstHookId = ProjectilePool.CreateSkillshotPool(e, id, HookProjectilePoolSize, HookProjectileSpeedMilliTilesPerSecond, HookHitRadius,
                    renderableType: RenderableType.Hook,
                    damageMultiplier: HookDamageMultiplier,
                    onHit: new ProjectileOnHitComponent
                    {
                        EffectType             = ProjectileOnHitEffectType.Hook,
                        HookPullBehindOffset   = HookPullBehindOffset,
                        HookPullDurationSeconds = HookPullDurationSeconds,
                    },
                    stopOnFirstHit: true);
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = HookProjectilePoolSize,
                    NextProjectileId = firstHookId,
                });
            },
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id = AbilityManager.HookAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(HookAbilityCooldownSeconds),
            }),
        });
    }
}
