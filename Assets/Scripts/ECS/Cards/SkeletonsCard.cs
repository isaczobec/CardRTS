using System;
using System.Collections.Generic;
using UnityEngine;

// Spawns 8 weak skeletons in a ring around the played point. Each skeleton is scaled off
// BasicMeleeTroopCard's own baseline (slightly lower move speed, 1/6 its health, half its
// attack damage, same attack speed — see each stat constant's own comment), takes 75% less
// damage against buildings (BuildingDamageBonusComponent — see BuildingDamageBonusSystem —
// with a NEGATIVE ratio; the same component GoblinSnatcherCard/StoneConstructCard use for a
// bonus works just as well for a penalty), and carries an OnKillScheduleComponent (see
// OnKillScheduleSystem) that raises a fresh friendly skeleton where an enemy troop died,
// SkeletonResurrectDelaySeconds after the kill — server-only (see ResolveSkeletonSummon).
// That new skeleton is spawned via the same SpawnSingleSkeleton helper as the initial 8, so
// it also carries its own OnKillScheduleComponent — the "legion" keeps growing on every kill,
// not just the original batch.
public class SkeletonsCard : SpawnAtPointCard
{
    private const int SkeletonCount = 8;
    private const float SpawnRadius = 4f;

    // Slightly lower than BasicMeleeTroopCard.Speed (50).
    private const int Speed = 45;
    // 1/6 of BasicMeleeTroopCard.MaxHealth (250), rounded.
    private const int MaxHealth = 42;
    private const int Range = 5;
    // Unchanged from BasicMeleeTroopCard.
    private const int Armor = 20;
    // Half of BasicMeleeTroopCard.Damage (34).
    private const int Damage = 17;
    // Unchanged from BasicMeleeTroopCard.
    private const float AttackSpeedMilliseconds = 333f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // Unchanged from BasicMeleeTroopCard.
    private const float DetectionRangeMultiplier = 72f; // 6x (explicit design ask) from 12
    // Same neutral:enemy ratio as SkillshotRangedTroopCard's own tuning (explicit design ask)
    // — back down at the pre-6x-bump value; see BasicMeleeTroopCard's own identical comment.
    private const float EnemyDetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 124.8f; // +30% (explicit design ask) from 96
    private const float AttackRangeMultiplier = 1.5f;
    private const float CooldownMultiplier = 3f;

    // 75% REDUCED damage vs. buildings — a negative BonusRatio on the same
    // BuildingDamageBonusComponent GoblinSnatcherCard/StoneConstructCard use for a bonus
    // (BuildingDamageBonusSystem's formula, Amount * (1 + BonusRatio), works the same either
    // direction): 1 + (-0.75) = 0.25, i.e. 25% of normal damage.
    private const float BuildingDamageBonusRatio = -0.75f;

    // How long after a kill the resurrected skeleton rises — see OnKillScheduleComponent.
    private const float SkeletonResurrectDelaySeconds = 0.5f;

    private const float MaxDistanceFromBuilding = 20f;

    static SkeletonsCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.SkeletonSummonResolve, ResolveSkeletonSummon);
    }

    public override int ShopGoldCost => 120;

    public override CardType Type => CardType.Skeletons;
    public override string Title => "Skeletons";
    public override string ImageName => "Skeletons";
    public override string Description => "Raises 8 skeletons in a ring. Each fallen enemy troop a skeleton kills rises again as a new friendly skeleton.";
    public override string IndicatorPrefabName => "Skeletons";

    // Previews all 8 landing spots (same radius/angles OnPlayed itself spawns at) instead
    // of a single indicator sitting at the cursor — see CircleIndicatorHelper.
    public override void OnIndicatorSpawned(GameObject indicator)
        => CircleIndicatorHelper.ArrangeInRing(indicator, SkeletonCount, SpawnRadius);

    // Flipped to wood-heavy (sum unchanged, 150) — 8 fragile 1/6-health skeletons is about
    // as "light" as this roster gets.
    private static readonly ResourceCost TotalCost = new ResourceCost
    {
        Wood = 100,
        Stone = 50
    };

    // Each of the 8 skeletons (including ones later raised by ResolveSkeletonSummon) is
    // worth an even split of the card's own cost — see ResourceValueComponent.
    private static readonly ResourceCost PerSkeletonValue = ResourceValueHelper.Split(TotalCost, SkeletonCount);

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

    // Spawns all 8 in a fixed (not randomized — this is predicted client-side, so it must
    // stay deterministic) ring around (x, y), same angular-spacing pattern as
    // AbilityManager.BuildRingOfProjectilesAbility. Returns the first skeleton's id, since
    // OnPlayed can only report one entity — see SpawnAtPointCard.OnPlayed's own doc comment.
    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        ulong firstId = 0;
        for (int i = 0; i < SkeletonCount; i++)
        {
            float angle = i * (360f / SkeletonCount) * Mathf.Deg2Rad;
            float spawnX = x + Mathf.Cos(angle) * SpawnRadius;
            float spawnY = y + Mathf.Sin(angle) * SpawnRadius;

            ulong id = SpawnSingleSkeleton(ecs, ownerPlayerId, spawnX, spawnY, cardEntityId);
            if (i == 0) firstId = id;
        }
        return firstId;
    }

    // Even split of TroopCardHelper's own default per-troop Gold drop, across every skeleton
    // this card can ever field (the initial ring of 8 AND any later resurrection — both
    // funnel through this same helper) — so the whole card still only drops that much total
    // if every one of them is killed, rather than that amount 8x over.
    private static readonly int GoldDropOnDeath = Mathf.RoundToInt((float)TroopCardHelper.DefaultGoldDropOnDeath / SkeletonCount);

    // cardEntityId is the Skeletons card instance every skeleton this helper ever spawns
    // (the initial ring of 8 AND any later resurrection) counts as being spawned by — see
    // SpawnedByCardComponent — so the card can't return to its owner's hand/deck until every
    // one of them (however many the legion has grown to) has died.
    private static ulong SpawnSingleSkeleton(ECS ecs, ushort ownerPlayerId, float x, float y, ulong cardEntityId)
    {
        StatsComponent stats = BuildStats();

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.Skeleton, stats, new List<Action<ECS, ulong>>
        {
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = DetectionRangeMultiplier,
                EnemyDetectionRangeMultiplier = EnemyDetectionRangeMultiplier,
                ChaseRangeMultiplier     = ChaseRangeMultiplier,
                AttackRangeMultiplier    = AttackRangeMultiplier,
                CooldownMultiplier       = CooldownMultiplier,
            }),
            // Indefinite modifier (TicksRemaining = int.MaxValue, no ActivatableComponent —
            // see GoblinSnatcherCard/StoneConstructCard for the same shape) rather than a
            // component directly on the troop, so it composes correctly with any other
            // modifier of this kind.
            (e, id) =>
            {
                EntityHandle modifier = e.CreateEntity();
                e.AddComponent(modifier.Id, new ModifierComponent
                {
                    TargetEntityId = id,
                    TicksRemaining = int.MaxValue,
                });
                e.AddComponent(modifier.Id, new BuildingDamageBonusComponent
                {
                    BonusRatio = BuildingDamageBonusRatio,
                });
            },
            // Innate trait, not a modifier — see OnKillScheduleComponent's own doc comment.
            (e, id) => e.AddComponent(id, new OnKillScheduleComponent
            {
                CallType          = ScheduledCallType.SkeletonSummonResolve,
                DelayTicks        = TickManager.SecondsToTicks(SkeletonResurrectDelaySeconds),
                AllowBuildingKills = false,
            }),
            // See ResourceValueComponent/PerSkeletonValue — applies to both the initial ring
            // of 8 and any later resurrection, since both funnel through this same helper.
            (e, id) => ResourceValueHelper.Attach(e, id, ownerPlayerId, PerSkeletonValue),
            (e, id) => SpawnedByCardHelper.Attach(e, id, cardEntityId),
        }, goldDropOnDeath: GoldDropOnDeath);
    }

    // The actual resurrection — deferred out to ScheduledCallSystem by OnKillScheduleSystem
    // once a skeleton lands a kill (see that system for exactly when/why). call.Param0 is
    // the killer's own entity id (used only to resolve which player owns the new skeleton —
    // a killer that has itself died before this fires means the resurrection silently
    // doesn't happen, same limitation every other ScheduledCallSystem resolver in this
    // codebase already has); call.Param1/Param2 are the world position the kill happened at.
    private static void ResolveSkeletonSummon(ECS ecs, ScheduledCallComponent call)
    {
        // Spawning a brand new troop entity must not be predicted — a client that also
        // created one here would end up with an extra, never-reconciled "ghost" skeleton
        // alongside the server's authoritative one (mirrors AbilityManager.
        // BuildAoeSpellCloneAbility's own isServer guard for the same reason).
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ulong killerId = call.Param0;
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(killerId)) return;

        ushort ownerPlayerId = troopStore.GetComponent(killerId).OwnerPlayerId;

        // Inherits the same card instance the killer skeleton was itself spawned by — the
        // original played-card entity id isn't otherwise available at this deferred spawn
        // site (this may be reached from an EphemeralSkeletonsCard-spawned killer too, since
        // both cards register this same resolver — the resurrected skeleton then correctly
        // belongs to THAT card instance instead).
        ulong cardEntityId = SpawnedByCardHelper.ResolveCardEntityId(ecs, killerId);
        SpawnSingleSkeleton(ecs, ownerPlayerId, call.Param1, call.Param2, cardEntityId);
    }
}
