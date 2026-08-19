using System;
using System.Collections.Generic;
using UnityEngine;

// A BasicRangedTroopCard variant — same baseline stats, just a tiny bit slower, and its
// pooled projectiles (RenderableType.PresentProjectile) carry a ProjectileOnHitComponent
// (EffectType.Aoe — see ProjectileOnHitSystem.ApplyAoe) instead of firing plain single-target
// shots: each hit also deals instant AOE damage to every enemy troop within
// AoeRadiusMultiplier x this troop's own Range stat of the impact point (including whatever
// it directly hit, which still also takes the normal per-shot hit damage every projectile
// deals regardless of EffectType), for AoeDamageRatio x this troop's own Damage stat.
//
// Also carries an OnHitScheduleComponent (see OnHitScheduleSystem) with PeriodHits = 8 and
// RequireEnemyTroopHit = true: every 8th hit this troop lands against an enemy troop
// (building hits don't count), ResolveSnatcherReinforcement spawns a weaker, temporary
// (15s LifetimeComponent) Goblin Snatcher next to it — server-only, per the explicit design
// ask — reusing GoblinSnatcherCard's own DefaultStats (halved) rather than duplicating its
// numbers, so the reinforcement stays in step if that card's balance ever changes.
//
// Also carries a permanent BuildingDamageBonusComponent modifier with a NEGATIVE ratio —
// 15% REDUCED damage against buildings (see BuildingDamageBonusRatio).
public class SantaClausCard : SpawnAtPointCard
{
    // Unchanged from BasicRangedTroopCard — see its own comment for the balance baseline
    // these are scaled from.
    private const int MaxHealth = 250;
    // A tiny bit slower than BasicRangedTroopCard.Speed (60) — explicit design ask.
    private const int Speed = 55;
    private const int Range = 12;
    private const int Armor = 20;
    private const int Damage = 17;
    private const float AttackSpeedMilliseconds = 500f;
    // Troops resist Spell damage 0 by default — only buildings do (see BuildingSpawnHelper).
    private const int SpellResist = 0;

    private const float DetectionRangeMultiplier = 18f; // 6x (explicit design ask) from 3
    // Same neutral:enemy ratio as SkillshotRangedTroopCard's own tuning (explicit design ask)
    // — back down at the pre-6x-bump value; see BasicRangedTroopCard's own identical comment.
    private const float EnemyDetectionRangeMultiplier = 3f;
    private const float ChaseRangeMultiplier = 26f; // +30% (explicit design ask) from 20
    private const float AttackRangeMultiplier = 1.5f;
    private const float WindDownMultiplier = 2f;
    private const int ProjectilePoolSize = 16;
    private const int ProjectileSpeedMilliTilesPerSecond = 15000; // 15 tiles/sec

    // Aoe on-hit effect — see ProjectileOnHitComponent/ProjectileOnHitSystem.ApplyAoe.
    // Radius multiplier mirrors FireManCard's own BurnSplashRangeMultiplier convention
    // (0.3x Range); damage ratio is a fraction of this troop's own Damage stat.
    private const float AoeRadiusMultiplier = 0.8f;
    private const float AoeDamageRatio = 0.7f;

    private const float MaxDistanceFromBuilding = 20f;

    // 15% REDUCED damage vs. buildings — a negative BonusRatio on the same
    // BuildingDamageBonusComponent GoblinSnatcherCard/StoneConstructCard use for a bonus
    // (BuildingDamageBonusSystem's formula, Amount * (1 + BonusRatio), works the same either
    // direction): 1 + (-0.15) = 0.85, i.e. 85% of normal damage.
    private const float BuildingDamageBonusRatio = -0.15f;

    // Snatcher-reinforcement on-hit effect — see OnHitScheduleComponent/ResolveSnatcher
    // Reinforcement. Both PeriodHits and HitsUntilProc are set to the same value at spawn
    // time so the FIRST reinforcement also waits out a full 8 hits, rather than firing early
    // on hit 1 (OnHitScheduleComponent's own default — HitsUntilProc left at 0 — procs
    // immediately on the first hit and only settles into the period afterward).
    private const int SnatcherReinforcementPeriodHits = 8;
    private const float SnatcherReinforcementLifetimeSeconds = 15f;
    // World units from Santa's own position the reinforcement spawns at — simple fixed
    // offset (deterministic — this must resolve identically on the server and every
    // predicting client), same spirit as DisplacementSystem's own fixed push distances.
    private const float SnatcherReinforcementSpawnOffset = 2f;
    // "Weaker" — see ResolveSnatcherReinforcement, which halves GoblinSnatcherCard's own
    // MaxHealth/Damage rather than redeclaring fresh numbers here.
    private const float SnatcherReinforcementStatRatio = 0.5f;
    // Matches GoblinSnatcherCard's own BuildingDamageBonusRatio (private there, so restated
    // here) — the reinforcement is thematically still a Goblin Snatcher.
    private const float SnatcherReinforcementBuildingDamageBonusRatio = 0.2f;

    static SantaClausCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.SantaSnatcherSpawnResolve, ResolveSnatcherReinforcement);
    }

    public override CardType Type => CardType.SantaClaus;
    public override string Title => "Santa Claus";
    public override string ImageName => "SantaClaus";
    public override string Description => "A ranged troop whose presents explode on impact, dealing area damage around the hit.";
    public override string IndicatorPrefabName => "SantaClaus";

    public override StatsComponent DefaultStats => BuildStats();
    // Metal folded into Wood/Stone, sum unchanged (190) — moderate weight ranged troop.
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 110,
            Stone = 80
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    public override int ShopGoldCost => 110;

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

        return TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, x, y, RenderableType.SantaClaus, stats, new List<Action<ECS, ulong>>
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
                ulong firstProjectileId = ProjectilePool.CreatePool(e, id, ProjectilePoolSize, ProjectileSpeedMilliTilesPerSecond,
                    renderableType: RenderableType.PresentProjectile,
                    onHit: new ProjectileOnHitComponent
                    {
                        EffectType         = ProjectileOnHitEffectType.Aoe,
                        AoeRadiusMultiplier = AoeRadiusMultiplier,
                        AoeDamageRatio      = AoeDamageRatio,
                    });
                e.AddComponent(id, new ProjectileOwnerComponent
                {
                    MaxProjectiles   = ProjectilePoolSize,
                    NextProjectileId = firstProjectileId,
                });
            },
            // Innate trait, not a modifier — see OnHitScheduleComponent's own doc comment.
            (e, id) => e.AddComponent(id, new OnHitScheduleComponent
            {
                CallType           = ScheduledCallType.SantaSnatcherSpawnResolve,
                DelayTicks         = 0,
                PeriodHits         = SnatcherReinforcementPeriodHits,
                HitsUntilProc      = SnatcherReinforcementPeriodHits,
                RequireEnemyTroopHit = true,
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
        });
    }

    // The actual reinforcement spawn — deferred out to ScheduledCallSystem by
    // OnHitScheduleSystem once this Santa's 8th qualifying hit lands (see that system for
    // exactly when/why; DelayTicks = 0 resolves as soon as ScheduledCallSystem next runs —
    // effectively the very next tick, same as StalkerCard's own ambush). call.Param0 is
    // Santa's own entity id; call.Param3 (whatever it just hit) is unused here.
    //
    // Spawning a brand new troop entity must not be predicted — a client that also created
    // one here would end up with an extra, never-reconciled "ghost" snatcher alongside the
    // server's authoritative one (mirrors SkeletonsCard.ResolveSkeletonSummon's own isServer
    // guard, per the explicit "spawn on the server" design ask).
    private static void ResolveSnatcherReinforcement(ECS ecs, ScheduledCallComponent call)
    {
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ulong santaId = call.Param0;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (posStore == null || troopStore == null) return;
        if (!posStore.HasComponent(santaId) || !troopStore.HasComponent(santaId)) return;

        PositionComponent santaPos = posStore.GetComponent(santaId);
        ushort ownerPlayerId = troopStore.GetComponent(santaId).OwnerPlayerId;
        float spawnX = santaPos.X + SnatcherReinforcementSpawnOffset;
        float spawnY = santaPos.Y;

        if (!CardRegistry.TryGet(CardType.GoblinSnatcher, out Card card)) return;
        StatsComponent goblinStats = card.DefaultStats;

        // Not bought outright — valued as a fraction of a real Goblin Snatcher purchase,
        // scaled by the same ratio its stats are (see ResourceValueComponent).
        ResourceCost reinforcementValue = ResourceValueHelper.Scale(card.Cost, SnatcherReinforcementStatRatio);

        StatsComponent weakStats = new StatsComponent
        {
            MaxHealth   = Mathf.RoundToInt(goblinStats.MaxHealth * SnatcherReinforcementStatRatio),
            Speed       = goblinStats.Speed,
            Range       = goblinStats.Range,
            Armor       = goblinStats.Armor,
            Damage      = Mathf.RoundToInt(goblinStats.Damage * SnatcherReinforcementStatRatio),
            AttackSpeed = goblinStats.AttackSpeed,
            SpellResist = goblinStats.SpellResist,
        };

        int lifetimeTicks = TickManager.SecondsToTicks(SnatcherReinforcementLifetimeSeconds);

        // Inherits the same card instance Santa himself was spawned by — the original
        // played-card entity id isn't otherwise available at this deferred spawn site (see
        // SkeletonsCard.ResolveSkeletonSummon for the identical pattern).
        ulong cardEntityId = SpawnedByCardHelper.ResolveCardEntityId(ecs, santaId);

        TroopCardHelper.SpawnTroop(ecs, ownerPlayerId, spawnX, spawnY, RenderableType.GoblinSnatcher, weakStats, new List<Action<ECS, ulong>>
        {
            // Matches GoblinSnatcherCard's own AI multipliers (private there, so restated
            // here — same reasoning as SnatcherReinforcementBuildingDamageBonusRatio above),
            // including its own 6x-detection/+30%-chase bump.
            (e, id) => e.AddComponent(id, new BasicMeleeAIComponent
            {
                DetectionRangeMultiplier = 72f,
                ChaseRangeMultiplier     = 124.8f,
                AttackRangeMultiplier    = 1.5f,
                CooldownMultiplier       = 2f,
            }),
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
                    BonusRatio = SnatcherReinforcementBuildingDamageBonusRatio,
                });
            },
            (e, id) => e.AddComponent(id, new LifetimeComponent
            {
                TicksRemaining        = lifetimeTicks,
                InitialTicksRemaining = lifetimeTicks,
                ShowTimer              = true,
            }),
            (e, id) => ResourceValueHelper.Attach(e, id, ownerPlayerId, reinforcementValue),
            (e, id) => SpawnedByCardHelper.Attach(e, id, cardEntityId),
        });
    }
}