using System;
using System.Collections.Generic;
using UnityEngine;

// Spawns 2 orcs in a ring around the played point — each a weaker pair-mate of
// BasicMeleeTroopCard ("the Warrior"): a little slower (Speed), 75% of his Damage/Armor, 75%
// of his attack RATE (AttackSpeedMilliseconds is a cooldown period — lower is faster — so
// "75% attack speed" means a LONGER period, attacking 25% less often, not a shorter one), and
// 70% of his MaxHealth. Same BasicMeleeAIComponent behavior as the Warrior otherwise
// (detection/chase/attack range multipliers, cooldown multiplier all unchanged).
public class OrcsCard : SpawnAtPointCard
{
    private const int OrcCount = 2;
    private const float SpawnRadius = 3f;

    // A little slower than BasicMeleeTroopCard.Speed (50) — explicit design ask, mirrors
    // PirateCard's own "a little worse" Speed value.
    private const int Speed = 44;
    // Unchanged from BasicMeleeTroopCard.
    private const int Range = 5;
    // 75% of BasicMeleeTroopCard.Damage (34) — explicit design ask.
    private const int Damage = 26;
    // 75% of BasicMeleeTroopCard.Armor (20) — explicit design ask.
    private const int Armor = 15;
    // 70% of BasicMeleeTroopCard.MaxHealth (250) — explicit design ask.
    private const int MaxHealth = 175;
    // 75% of BasicMeleeTroopCard's own attack RATE — see this class's own doc comment for
    // why that means dividing (not multiplying) the millisecond period by 0.75.
    private const float BaseAttackSpeedMilliseconds = 333f;
    private const float AttackSpeedMilliseconds = BaseAttackSpeedMilliseconds / 0.75f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // Unchanged from BasicMeleeTroopCard.
    private const float DetectionRangeMultiplier = 48f;
    private const float ChaseRangeMultiplier = 96f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    private const float MaxDistanceFromBuilding = 20f;

    // A bit more than BasicMeleeTroopCard's own 100/{Wood 120, Stone 30} — explicit design
    // ask ("cost a bit more than the basic melee troop card") — reasonable given this spawns
    // two bodies per play.
    public override int ShopGoldCost => 130;

    public override CardType Type => CardType.Orcs;
    public override string Title => "Orcs";
    public override string ImageName => "Orcs";
    public override string Description => "Spawns 2 orcs, each weaker than a lone Warrior (75% damage/armor/attack speed, 70% health, a little slower) but coming as a pair.";
    public override string IndicatorPrefabName => "Orcs";

    // Previews both landing spots (same radius/angles OnPlayed itself spawns at) instead of
    // a single indicator sitting at the cursor — see CircleIndicatorHelper.
    public override void OnIndicatorSpawned(GameObject indicator)
        => CircleIndicatorHelper.ArrangeInRing(indicator, OrcCount, SpawnRadius);

    private static readonly ResourceCost TotalCost = new ResourceCost
    {
        Wood  = 150,
        Stone = 40,
    };

    // Each of the 2 orcs is worth an even split of the card's own cost — see
    // ResourceValueComponent.
    private static readonly ResourceCost PerOrcValue = ResourceValueHelper.Split(TotalCost, OrcCount);

    // Even split of TroopCardHelper's own default per-troop Gold drop, across both orcs —
    // so the whole card still only drops that much total if both are killed, rather than
    // that amount twice over.
    private static readonly int GoldDropOnDeath = Mathf.RoundToInt((float)TroopCardHelper.DefaultGoldDropOnDeath / OrcCount);

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => TotalCost;
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

    // Spawns both in a fixed (not randomized — this is predicted client-side, so it must
    // stay deterministic) ring around (x, y), same angular-spacing pattern as
    // SkeletonsCard.OnPlayed. Returns the first orc's id, since OnPlayed can only report one
    // entity — see SpawnAtPointCard.OnPlayed's own doc comment.
    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        ulong firstId = 0;
        for (int i = 0; i < OrcCount; i++)
        {
            float angle = i * (360f / OrcCount) * Mathf.Deg2Rad;
            float spawnX = x + Mathf.Cos(angle) * SpawnRadius;
            float spawnY = y + Mathf.Sin(angle) * SpawnRadius;

            ulong id = SpawnSingleOrc(ecs, ownerPlayerId, spawnX, spawnY, cardEntityId);
            if (i == 0) firstId = id;
        }
        return firstId;
    }

    private static ulong SpawnSingleOrc(ECS ecs, ushort ownerPlayerId, float x, float y, ulong cardEntityId)
    {
        StatsComponent stats = BuildStats();

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.Orc, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // See ResourceValueComponent/PerOrcValue.
            (e, id) => ResourceValueHelper.Attach(e, id, ownerPlayerId, PerOrcValue),
            (e, id) => SpawnedByCardHelper.Attach(e, id, cardEntityId),
        }, goldDropOnDeath: GoldDropOnDeath);
    }
}
