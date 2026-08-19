using System.Collections.Generic;
using UnityEngine;

// A defensive turret building like CannonCard — same shared BuildingSpawnHelper/
// TurretAIComponent/TurretAISystem plumbing — but with much higher Range, and firing in
// TurretProjectileMode.Ballistic instead of Pooled: its "shots" are cosmetic-only
// BallisticProjectileComponent entities with no hitbox at all (see TurretAISystem.
// FireBallistic), dealing AOE damage in a radius around wherever the target stood at the
// moment of firing once their flight ends — see ResolveMissileImpact, deferred out to
// ScheduledCallSystem for exactly the flight's own duration.
//
// TurretAIComponent.CanTargetEnemyBuildings and CanTargetNeutralBuildings are both true —
// this silo can also fire at an enemy-owned building, or (failing that) a neutral-owned one
// (a world-gen resource node/tree), when no enemy troop is in range — see TurretAISystem's
// own targeting-priority comment for the full enemy troop > enemy building > neutral
// building order. It also carries a permanent BuildingDamageBonusComponent modifier with a
// NEGATIVE ratio (60% REDUCED damage vs. buildings) — the same BuildingDamageBonusSystem
// every other building-damage-bonus card already uses, which applies automatically to
// WHATEVER building the AOE blast hits (the one it deliberately targeted, or any other
// building simply caught in the radius), no special-casing needed in the AOE resolution
// itself.
public class MissileSiloCard : SpawnAtPointCard
{
    // Cut by 30% (explicit design ask) from the original 270.
    private const int MaxHealth = 189;
    // Much higher than CannonCard's own Range (14) — explicit design ask. Was 300; cut by
    // 55% to 135, then cut a further 45% to 61 (both explicit design asks, compounding —
    // since with MinRange below now also gating the near edge, the old value made for an
    // enormous engagement band), then cut a further 40% (explicit design ask) to the
    // current value.
    private const int Range = 37;
    // Hard floor on engagement distance (world units, not a multiple of Range — see
    // TurretAIComponent.MinRange) — a long-range siege piece shouldn't be able to blast
    // something standing right next to it; a target has to close inside this to be safe.
    // Not scaled by AttackRangeMultiplier (that's leeway on the FAR edge only) — this is a
    // flat near-edge cutoff. Roughly Cannon's own whole Range (14), so anything within a
    // Cannon's typical engagement distance is also safe from the Silo.
    private const float MinRange = 20f;
    // -40% (explicit design ask) from 32.
    private const int Damage = 19;
    // Slow reload — a long-range siege piece, not a rapid-fire defense.
    private const float AttackSpeedMilliseconds = 3000f;

    private const float ActivationDelaySeconds = 2f;

    // Missile flight speed (world units/second) — distinct from CannonCard's own
    // milli-tiles/sec pooled-projectile speed, since a ballistic shot has no
    // SeekingProjectileComponent at all; see TurretAISystem.FireBallistic.
    private const float MissileSpeedTilesPerSecond = 50f;

    // Multiple of this card's own Range stat — the AOE damage radius when a missile's flight
    // ends (see BallisticProjectileComponent.ImpactRadius/ResolveMissileImpact). Scales
    // automatically with every cut to Range above, including the most recent -40%.
    private const float ImpactRadiusMultiplier = 0.085f;

    // Small leeway (as a multiple of Range) allowed when re-checking the target is still in
    // range once the windup finishes — mirrors CannonCard's own AttackRangeMultiplier.
    private const float AttackRangeMultiplier = 1.1f;
    // Multiple of the attack windup (AttackSpeed ticks) the post-shot wind-down lasts —
    // mirrors CannonCard's own WindDownMultiplier.
    private const float WindDownMultiplier = 1.2f;

    // 60% REDUCED damage vs. buildings — a negative BonusRatio on the same
    // BuildingDamageBonusComponent GoblinSnatcherCard/StoneConstructCard use for a bonus
    // (BuildingDamageBonusSystem's formula, Amount * (1 + BonusRatio), works the same either
    // direction): 1 + (-0.6) = 0.4, i.e. 40% of normal damage.
    private const float BuildingDamageBonusRatio = -0.3f;

    // Gold dropped to whoever destroys this building — mirrors CannonCard/BuildingCard's own.
    // +30% (explicit design ask) from 40.
    private const int GoldDropOnDeath = 52;

    private const float MaxDistanceFromBuilding = 30f;

    // Fallbacks for StatsQuery.GetRange/GetDamage when the launching silo somehow has no
    // StatsComponent by resolve time — matches ProjectileOnHitSystem's own
    // BurnFallbackRange/BurnFallbackDamage convention rather than 0.
    private const int ImpactFallbackRange = 10;
    private const int ImpactFallbackDamage = 10;

    private static readonly List<ulong> _impactBuffer = new List<ulong>();

    static MissileSiloCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.MissileImpactResolve, ResolveMissileImpact);
    }

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.MissileSilo;
    public override CardCategory Category => CardCategory.Building;
    public override string Title => "Missile Silo";
    public override string ImageName => "MissileSilo";
    public override string Description => "A long-range stationary silo that fires missiles dealing area damage on impact. Can also strike neutral structures, but prefers enemy troops.";
    public override string IndicatorPrefabName => "MissileSilo";

    // Speed is STAT_NA (not just 0) — this building doesn't move, so that row shouldn't be
    // shown on the card face at all. Range/Damage/AttackSpeed ARE real, unlike BuildingCard.
    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = StatsComponent.STAT_NA,
        Range       = Range,
        Armor       = BuildingSpawnHelper.Armor,
        Damage      = Damage,
        AttackSpeed = TickManager.MillisecondsToTicks(AttackSpeedMilliseconds),
        SpellResist = BuildingSpawnHelper.SpellResist,
    };

    // Metal folded into Wood/Stone (220 sum) — lower MaxHealth (189) than Cannon, so less
    // stone-heavy. Gems=25 also folded in as a further +50 Wood/+50 Stone (explicit design
    // ask — troops/buildings no longer cost Gems at all).
    public override ResourceCost Cost => new ResourceCost
        {
            Wood  = 150,
            Stone = 170,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        BuildingSpawnHelper.AddBuildingComponents(ecs, id, ownerPlayerId, RenderableType.MissileSilo, MaxHealth,
            (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds),
            range: Range, damage: Damage, attackSpeedTicks: TickManager.MillisecondsToTicks(AttackSpeedMilliseconds));

        ecs.AddComponent(id, new TurretAIComponent
        {
            WindDownMultiplier              = WindDownMultiplier,
            AttackRangeMultiplier           = AttackRangeMultiplier,
            MinRange                        = MinRange,
            CanTargetEnemyBuildings         = true,
            CanTargetNeutralBuildings       = true,
            ProjectileMode                  = TurretProjectileMode.Ballistic,
            BallisticRenderableType         = RenderableType.Missile,
            BallisticSpeedTilesPerSecond    = MissileSpeedTilesPerSecond,
            BallisticImpactRadiusMultiplier = ImpactRadiusMultiplier,
        });

        // Indefinite modifier (TicksRemaining = int.MaxValue, no ActivatableComponent — see
        // GoblinSnatcherCard/SantaClausCard for the same shape) rather than a component
        // directly on the entity, so it composes correctly with any other modifier of this
        // kind.
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = id,
            TicksRemaining = int.MaxValue,
        });
        ecs.AddComponent(modifier.Id, new BuildingDamageBonusComponent
        {
            BonusRatio = BuildingDamageBonusRatio,
        });

        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Gold = GoldDropOnDeath
        } } );

        return id;
    }

    // The actual missile AOE damage — deferred out to ScheduledCallSystem by
    // TurretAISystem.FireBallistic for exactly the flight's own duration (see that method for
    // why). call.Param0 is the launching silo's own entity id (used to resolve owner/Range/
    // Damage live, and as DealerEntityId on every DamageRequest — that's also what lets
    // BuildingDamageBonusSystem's existing -60%-vs-buildings modifier apply automatically to
    // any building this blast hits, with no special-casing needed here); call.Param1/Param2
    // is the impact point (captured at launch time — the target may have moved, died, or
    // stopped existing by the time this fires, so this always lands where it was AIMED, not
    // wherever the original target ended up).
    //
    // A silo destroyed mid-flight means this silently deals no damage at all (can't resolve
    // Range/Damage/owner without it) — the same limitation every other ScheduledCallSystem
    // resolver in this codebase already accepts (see e.g. AoeRootCard.ResolveAoeRoot).
    private static void ResolveMissileImpact(ECS ecs, ScheduledCallComponent call)
    {
        ulong siloId = call.Param0;
        float x = call.Param1;
        float y = call.Param2;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(siloId)) return;

        ushort ownerPlayerId = troopStore.GetComponent(siloId).OwnerPlayerId;
        float radius = StatsQuery.GetRange(ecs, siloId, ImpactFallbackRange) * ImpactRadiusMultiplier;
        int damage = StatsQuery.GetDamage(ecs, siloId, ImpactFallbackDamage);

        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();

        _impactBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(x, y, radius, _impactBuffer);

        foreach (ulong targetId in _impactBuffer)
        {
            if (targetId == siloId) continue;
            if (!troopStore.HasComponent(targetId)) continue;

            TroopComponent target = troopStore.GetComponent(targetId);
            if (target.OwnerPlayerId == ownerPlayerId) continue; // never friendly fire
            if (target.IsDead) continue;
            if (healthStore == null || !healthStore.HasComponent(targetId)) continue;
            if (!ActivationQuery.IsActivated(ecs, targetId)) continue;

            ecs.Requests.CreateRequest(new DamageRequest(targetId, damage) { DealerEntityId = siloId });
        }
    }
}
