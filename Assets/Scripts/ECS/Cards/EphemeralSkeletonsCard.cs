using System;
using System.Collections.Generic;
using UnityEngine;

// Spawns 8 short-lived ranged skeletons in a ring around the played point — scaled off
// BasicRangedTroopCard's own baseline (higher attack speed, shorter range, lower health,
// slightly lower damage — see each stat constant's own comment). Each one carries a
// LifetimeComponent (see LifetimeSystem) so it expires EphemeralLifetimeSeconds after it
// actually activates (LifetimeSystem only burns TicksRemaining once ActivationQuery.IsActive
// is true, so the deploy delay doesn't eat into the 20 seconds). Unlike SkeletonsCard's own
// skeletons, these do NOT get a BuildingDamageBonusComponent penalty — full building damage.
// They DO reuse SkeletonsCard's existing OnKillScheduleComponent/ScheduledCallType.
// SkeletonSummonResolve wiring, so a kill still raises a (regular, non-ephemeral, permanent)
// friendly Skeleton where the enemy died — see SkeletonsCard.ResolveSkeletonSummon.
public class EphemeralSkeletonsCard : SpawnAtPointCard
{
    private const int SkeletonCount = 8;
    private const float SpawnRadius = 4f;

    // Unchanged from BasicRangedTroopCard.
    private const int Speed = 60;
    // Much lower than BasicRangedTroopCard.MaxHealth (250) — these are meant to melt fast.
    private const int MaxHealth = 50;
    // Shorter than BasicRangedTroopCard.Range (19).
    private const int Range = 12;
    // Unchanged from BasicRangedTroopCard.
    private const int Armor = 20;
    // Slightly lower than BasicRangedTroopCard.Damage (34).
    private const int Damage = 17;
    // Higher attack speed (lower value) than BasicRangedTroopCard.AttackSpeedMilliseconds (800).
    private const float AttackSpeedMilliseconds = 500f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    // Unchanged from BasicRangedTroopCard.
    // 4x — explicit design ask.
    private const float DetectionRangeMultiplier = 12f;
    private const float ChaseRangeMultiplier = 20f;
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 3f;

    private const int ProjectilePoolSize = 64;
    private const int ProjectileSpeedMilliTilesPerSecond = 15000; // 15 tiles/sec

    // How long each skeleton survives once actually activated — see LifetimeComponent.
    private const float EphemeralLifetimeSeconds = 20f;

    // How long after a kill the (regular, non-ephemeral) resurrected skeleton rises — same
    // delay SkeletonsCard itself uses, kept in sync deliberately.
    private const float SkeletonResurrectDelaySeconds = 2f;

    private const float MaxDistanceFromBuilding = 20f;

    public override int ShopGoldCost => 110;

    public override CardType Type => CardType.EphemeralSkeletons;
    public override string Title => "Ephemeral Skeletons";
    public override string ImageName => "EphemeralSkeletons";
    public override string Description => "Raises 8 short-lived skeleton archers in a ring that crumble after 20 seconds. Any enemy troop one kills rises again as a permanent friendly skeleton.";
    public override string IndicatorPrefabName => "EphemeralSkeletons";

    // Previews all 8 landing spots (same radius/angles OnPlayed itself spawns at) instead
    // of a single indicator sitting at the cursor — see CircleIndicatorHelper.
    public override void OnIndicatorSpawned(GameObject indicator)
        => CircleIndicatorHelper.ArrangeInRing(indicator, SkeletonCount, SpawnRadius);

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Stone = 100,
            Metal = 40
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

    // Same fixed (deterministic — this is predicted client-side) ring layout as
    // SkeletonsCard.OnPlayed. Returns the first skeleton's id, since OnPlayed can only
    // report one entity — see SpawnAtPointCard.OnPlayed's own doc comment.
    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        ulong firstId = 0;
        for (int i = 0; i < SkeletonCount; i++)
        {
            float angle = i * (360f / SkeletonCount) * Mathf.Deg2Rad;
            float spawnX = x + Mathf.Cos(angle) * SpawnRadius;
            float spawnY = y + Mathf.Sin(angle) * SpawnRadius;

            ulong id = SpawnSingleEphemeralSkeleton(ecs, ownerPlayerId, spawnX, spawnY);
            if (i == 0) firstId = id;
        }
        return firstId;
    }

    private static ulong SpawnSingleEphemeralSkeleton(ECS ecs, ushort ownerPlayerId, float x, float y)
    {
        StatsComponent stats = BuildStats();

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.EphemeralSkeleton, stats, new List<Action<ECS, ulong>>
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
                ulong firstProjectileId = ProjectilePool.CreatePool(e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond);
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
            (e, id) =>
            {
                int lifetimeTicks = TickManager.SecondsToTicks(EphemeralLifetimeSeconds);
                e.AddComponent(id, new LifetimeComponent
                {
                    TicksRemaining        = lifetimeTicks,
                    InitialTicksRemaining = lifetimeTicks,
                    ShowTimer              = true,
                });
            },
            // Innate trait, not a modifier — see OnKillScheduleComponent's own doc comment.
            // Reuses SkeletonSummonResolve (registered by SkeletonsCard's static constructor)
            // so a kill raises a REGULAR (permanent) skeleton, per the request that these
            // ephemeral archers "should also spawn regular skeletons if they kill an enemy
            // troop" — no separate resolver needed.
            (e, id) => e.AddComponent(id, new OnKillScheduleComponent
            {
                CallType           = ScheduledCallType.SkeletonSummonResolve,
                DelayTicks         = TickManager.SecondsToTicks(SkeletonResurrectDelaySeconds),
                AllowBuildingKills = false,
            }),
        });
    }
}
