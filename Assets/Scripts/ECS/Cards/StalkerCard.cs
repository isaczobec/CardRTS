using System;
using System.Collections.Generic;

// A BasicMeleeTroopCard variant — same baseline stats, just slightly faster, and equipped
// with AbilityManager's Shadow Cloak ability (temporary untargetability). Its own dedicated
// model/prefab (see RenderableType.Stalker, wired to the "StalkerRenderer" scene object in
// RenderableManager); reuses BasicMeleeTroopCard's own IndicatorPrefabName, since the
// deploy-placement indicator doesn't need a troop-specific look.
public class StalkerCard : SpawnAtPointCard
{
    // Unchanged from BasicMeleeTroopCard — see its own comment for the balance baseline
    // these are scaled from.
    private const int MaxHealth = 250;
    // Slightly faster than BasicMeleeTroopCard.Speed (50) — explicit design ask.
    private const int Speed = 60;
    private const int Range = 5;
    private const int Armor = 20;
    private const int Damage = 34;
    private const float AttackSpeedMilliseconds = 333f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 24f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    // Cooldown for the troop's Shadow Cloak ability (see AbilityManager.ShadowCloakAbilityId)
    // — comfortably longer than the cloak's own 8s duration so there's real downtime between
    // casts, judgment call consistent with e.g. StoneConstructCard's GroundSlamCooldownSeconds.
    private const float ShadowCloakCooldownSeconds = 16f;

    // Pricier than BasicMeleeTroopCard (100) to match its added utility.
    public override int ShopGoldCost => 120;

    public override CardType Type => CardType.Stalker;
    public override string Title => "Stalker";
    public override string ImageName => "Stalker";
    public override string Description => "A swift melee troop that can cloak itself, becoming untargetable by enemies for a short time.";
    public override string IndicatorPrefabName => "Stalker";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 120,
            Stone = 30,
            Gems = 5,
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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.Stalker, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // Shadow Cloak (Q). Slots 2-4 (W/E/R) are left empty (0).
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id = AbilityManager.ShadowCloakAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(ShadowCloakCooldownSeconds),
            }),
        });
    }
}
