using System.Collections.Generic;
using UnityEngine;

// Ground-targeted spell — after ActivationDelaySeconds (the same "activation period" every
// SpawnAtPointCard-spawned entity gets, see ActivatableComponent), launches a missile from
// the casting player's own base (see PlayerBaseQuery) toward the targeted point. Reuses
// MissileSiloCard's own cosmetic-only flight entity shape (BallisticProjectileComponent +
// RenderableType.Missile + LifetimeComponent for the flight duration, no hitbox at all) —
// just constructed directly here instead of through TurretAISystem.FireBallistic, since this
// is a one-off cast rather than a persistent turret's repeating attack. Deals a flat 120
// Spell-type damage (not stat-derived) in an AOE once the missile lands.
//
// The missile entity is only ever created on the server (see ResolveLaunch's own isServer
// guard) — explicit design ask, so its id is never client-guessed and only ever comes from
// the server's delta stream. This mirrors SkeletonsCard.ResolveSkeletonSummon/
// SantaClausCard.ResolveSnatcherReinforcement's own reasoning: a predicting client that also
// created a missile entity here would end up with an extra, never-reconciled "ghost" one
// alongside the server's authoritative copy — and since MissileProjectileRenderer tracks/
// tears down its visual purely by entity id, that "ghost" would show as a second, incorrect
// missile.
public class BallisticMissileCard : SpawnAtPointCard
{
    private const float ActivationDelaySeconds = 1f;
    private const int Damage = 120;
    private const float ImpactRadius = 15f;
    // Matches MissileSiloCard's own BallisticSpeedTilesPerSecond convention.
    private const float MissileSpeedTilesPerSecond = 90f;

    private const float MaxDistanceFromBuilding = 200f;
    private const float MaxDistanceFromTroop = 15f;

    private static readonly List<ulong> _impactBuffer = new List<ulong>();

    static BallisticMissileCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.BallisticMissileLaunchResolve, ResolveLaunch);
        ScheduledCallSystem.RegisterCall(ScheduledCallType.BallisticMissileImpactResolve, ResolveImpact);
    }

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.BallisticMissile;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Ballistic Missile";
    public override string ImageName => "BallisticMissile";
    public override string Description => "After a short delay, launches a missile from your base that deals 120 spell damage in an area at the targeted point.";
    public override string IndicatorPrefabName => "BallisticMissile";

    // The indicator prefab's mesh is assumed to be authored at 1-unit diameter — mirrors
    // AoeSpellCard/AoeRootCard's own convention.
    public override void OnIndicatorSpawned(GameObject indicator)
    {
        float diameter = ImpactRadius * 2f;
        indicator.transform.localScale = new Vector3(diameter, 1f, diameter);
    }

    // Range shown on the card face is the actual impact AOE radius, mirroring AoeRootCard's
    // own "show the real affected radius" reasoning — Damage is real too, unlike most spells,
    // since it's a flat amount rather than stat-derived.
    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = Mathf.RoundToInt(ImpactRadius),
        Armor       = StatsComponent.STAT_NA,
        Damage      = Damage,
        AttackSpeed = StatsComponent.STAT_NA,
        SpellResist = StatsComponent.STAT_NA,
    };

    public override ResourceCost Cost => new ResourceCost
        {
            Metal = 100,
            Gems  = 15,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        // Telegraph/cast-marker entity — mirrors AoeRootCard's own shape (a non-physical
        // TroopComponent purely so OwnerPlayerId is resolvable, e.g. for
        // DeployProgressIndicatorManager's deploy-progress ring). Disappears right as the
        // missile actually launches.
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.BallisticMissileMarker });
        ecs.AddComponent(id, DefaultStats);
        ecs.AddComponent(id, new TroopComponent { OwnerPlayerId = ownerPlayerId, IsPhysicalTroop = false });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        int lifetimeTicks = TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new LifetimeComponent
        {
            TicksRemaining        = lifetimeTicks,
            InitialTicksRemaining = lifetimeTicks,
        });

        // Caster's owner id and the target point are captured here rather than re-read off
        // this entity at resolve time, since LifetimeComponent means it (and its components)
        // may already be gone by the moment the call fires — same reasoning as AoeRootCard's
        // own OnPlayed.
        int launchDelayTicks = TickManager.SecondsToTicks(ActivationDelaySeconds);
        ScheduledCallSystem.Schedule(ecs, ScheduledCallType.BallisticMissileLaunchResolve, launchDelayTicks,
            param0: ownerPlayerId, param1: x, param2: y);

        return id;
    }

    // Spawns the actual missile — deferred out to ScheduledCallSystem by OnPlayed for exactly
    // ActivationDelaySeconds. call.Param0 is the caster's own player id; call.Param1/Param2
    // the target point.
    private static void ResolveLaunch(ECS ecs, ScheduledCallComponent call)
    {
        // See this class's own doc comment for why this must not be predicted.
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ushort ownerPlayerId = (ushort)call.Param0;
        float targetX = call.Param1;
        float targetY = call.Param2;

        if (!PlayerBaseQuery.TryFindPosition(ecs, ownerPlayerId, out float launchX, out float launchY)) return;

        float distance = Vector2.Distance(new Vector2(launchX, launchY), new Vector2(targetX, targetY));
        float speed = Mathf.Max(0.01f, MissileSpeedTilesPerSecond);
        int flightTicks = Mathf.Max(1, TickManager.SecondsToTicks(distance / speed));

        EntityHandle missile = ecs.CreateEntity();
        ecs.AddComponent(missile.Id, new PositionComponent(launchX, launchY));
        ecs.AddComponent(missile.Id, new RenderableComponent { Type = RenderableType.Missile });
        ecs.AddComponent(missile.Id, new LifetimeComponent
        {
            TicksRemaining        = flightTicks,
            InitialTicksRemaining = flightTicks,
        });
        ecs.AddComponent(missile.Id, new BallisticProjectileComponent
        {
            TargetX      = targetX,
            TargetY      = targetY,
            ImpactRadius = ImpactRadius,
        });

        ScheduledCallSystem.Schedule(ecs, ScheduledCallType.BallisticMissileImpactResolve, flightTicks,
            param0: ownerPlayerId, param1: targetX, param2: targetY);
    }

    // The actual AOE damage — deferred out to ScheduledCallSystem by ResolveLaunch for
    // exactly the flight's own duration, mirrors MissileSiloCard.ResolveMissileImpact. No
    // isServer guard needed here (unlike ResolveLaunch above) — this only creates a
    // DamageRequest against an EXISTING entity, which is safe to predict/duplicate the same
    // way any other damage-dealing effect is (see HealthBarManager's own reasoning for
    // predicting DamageRequest immediately).
    private static void ResolveImpact(ECS ecs, ScheduledCallComponent call)
    {
        ushort ownerPlayerId = (ushort)call.Param0;
        float x = call.Param1;
        float y = call.Param2;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (troopStore == null) return;

        _impactBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(x, y, ImpactRadius, _impactBuffer);

        foreach (ulong targetId in _impactBuffer)
        {
            if (!troopStore.HasComponent(targetId)) continue;

            TroopComponent target = troopStore.GetComponent(targetId);
            if (target.OwnerPlayerId == ownerPlayerId) continue; // never friendly fire
            if (target.IsDead) continue;
            if (healthStore == null || !healthStore.HasComponent(targetId)) continue;
            if (!ActivationQuery.IsActivated(ecs, targetId)) continue;

            ecs.Requests.CreateRequest(new DamageRequest(targetId, Damage) { Type = DamageType.Spell });
        }
    }
}
