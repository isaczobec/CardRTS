using System;
using System.Collections.Generic;

// A BasicMeleeTroopCard ("Warrior") variant — same baseline shape (BasicMeleeAIComponent,
// same Speed/Range/Armor/AttackSpeed/SpellResist), but 60% more MaxHealth and 40% more
// Damage — explicit design ask. Equips two abilities (see AbilityManager):
// KineticPullAbilityId (Q) winds up briefly then pulls a nearby enemy toward the Knight
// (DisplacementSystem, same primitive GroundSlamAbilityId's push uses, just flipped toward
// the caster instead of away from it), on a 16s cooldown; KineticShieldAbilityId (W) grants
// itself a 100 HP damage-absorbing shield (BarrierComponent, same primitive BarrierCard
// grants a target) for 7 seconds, on a 25s cooldown.
public class KineticKnightCard : SpawnAtPointCard
{
    // Unchanged from BasicMeleeTroopCard ("the Warrior") — see its own comment for the
    // rebalance baseline these are scaled from.
    private const int Speed = 50;
    private const int Range = 5;
    private const int Armor = 20;
    private const float AttackSpeedMilliseconds = 333f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // 60% more than BasicMeleeTroopCard.MaxHealth (250) — explicit design ask.
    private const int MaxHealth = 400;
    // 40% more than BasicMeleeTroopCard.Damage (34), rounded — explicit design ask.
    private const int Damage = 48;

    // Unchanged from BasicMeleeTroopCard.
    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 96f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    // Cooldowns live here (not on the Ability definitions themselves — see AbilityComponent's
    // own doc comment) — both explicit design asks.
    private const float KineticPullCooldownSeconds = 16f;
    private const float KineticShieldCooldownSeconds = 25f;

    public override int ShopGoldCost => 160;

    public override CardType Type => CardType.KineticKnight;
    public override string Title => "Kinetic Knight";
    public override string ImageName => "KineticKnight";
    public override string Description => "A tougher, harder-hitting melee troop that can pull a nearby enemy in close, or shield itself from incoming damage.";
    public override string IndicatorPrefabName => "KineticKnight";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Wood  = 80,
            Metal = 40,
            Soulstones = 1,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override int[] GrantedAbilityIds => new[] { AbilityManager.KineticPullAbilityId, AbilityManager.KineticShieldAbilityId };

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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.KineticKnight, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            (e, id) => e.AddComponent(id, new AbilityComponent
            {
                Ability1Id            = AbilityManager.KineticPullAbilityId,
                Ability1CooldownTicks = TickManager.SecondsToTicks(KineticPullCooldownSeconds),
                Ability2Id            = AbilityManager.KineticShieldAbilityId,
                Ability2CooldownTicks = TickManager.SecondsToTicks(KineticShieldCooldownSeconds),
            }),
        });
    }
}
