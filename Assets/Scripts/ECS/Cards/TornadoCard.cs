using UnityEngine;
using System.Collections.Generic;

// Two-point spell (click a start point, then a destination, exactly like BlinkCard): after
// ActivationDelaySeconds, a single entity (created once here, at OnPlayed — see its own
// comment for why no separate telegraph-then-resolve split is needed) carrying a
// TornadoProjectileComponent forms at the start point and sweeps toward the destination at
// TilesPerSecond (see TornadoProjectileSystem, which does all the actual movement/pulling).
// Every enemy troop it passes over gets pulled along with it via PirateCard's own Hook
// primitive (DisplacementSystem.BeginDisplacement — see ProjectileOnHitSystem.ApplyHook),
// re-applied every tick it stays inside HitRadius, so the whole group gets dragged toward the
// destination rather than just knocked aside.
public class TornadoCard : MultiPointCard
{
    // How long after being played the tornado takes to actually start forming/moving —
    // slightly longer than the standard 1s (SpeedBoostCard/BarrierCard/HealCard/...) since
    // this is a strong area-control effect, mirrors BallisticMissileCard's own longer windup.
    private const float ActivationDelaySeconds = 1f;

    // Deliberately slow — the tornado needs to linger long enough over troops caught in it
    // to actually drag them along, not just clip past them.
    private const float TilesPerSecond = 10f;
    public const float HitRadius = 8f;

    // How far apart the two clicked points may be — i.e. the max distance the tornado can
    // sweep in one cast. Enforced client-side (CardHandRenderer/CardPlacementIndicatorManager
    // clamp both the preview and the point actually captured) and re-checked server-side
    // (MultiPointCardPlaySystem) — same shape as BlinkCard's own MaxBlinkDistance.
    private const float MaxTornadoDistance = 40f;

    private const float MaxDistanceFromBuilding = 80f;
    private const float MaxDistanceFromTroop = 45f;

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.Tornado;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Tornado";
    public override string ImageName => "Tornado";
    public override string Description => "Marks a start point and a destination. After a delay, a tornado forms at the start point and sweeps toward the destination, dragging every enemy troop it touches along with it.";
    public override string IndicatorPrefabName => "Tornado";

    public override int PointCount => 2;
    public override float MaxRangeFromPreviousPoint => MaxTornadoDistance;

    // MaxHealth/Speed/Armor/Damage/AttackSpeed/SpellResist are STAT_NA — this entity has no
    // HealthComponent and deals no damage at all; Range shows the real pull radius, mirrors
    // AoeRootCard/BallisticMissileCard's own "show the real affected radius" reasoning.
    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = Mathf.RoundToInt(HitRadius),
        Armor       = StatsComponent.STAT_NA,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
        SpellResist = StatsComponent.STAT_NA,
    };

    // Spells now cost only Gems (explicit design ask) — Wood/Metal folded into a single
    // Gems price roughly proportional to the old total resource investment (~8 non-gem
    // units per gem).
    public override ResourceCost Cost => new ResourceCost
        {
            Gems  = 22,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;
    public override bool AllowsFriendlyTroopRange() => true;

    // Same "1-unit-diameter mesh" convention as every other radius-scaled indicator
    // (AoeSpellCard/AoeRootCard/BlinkCard/...) — both points use the same prefab, scaled
    // identically, since HitRadius is the same at the start point as it ends up being for the
    // whole sweep.
    public override void OnIndicatorSpawned(GameObject indicator, int pointIndex)
    {
        float diameter = HitRadius * 2f;
        indicator.transform.localScale = new Vector3(diameter, 1f, diameter);
    }

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, IReadOnlyList<Vector2> points)
    {
        Vector2 source = points[0];
        Vector2 destination = points[1];

        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(source.x, source.y));
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.Tornado });
        ecs.AddComponent(id, DefaultStats);

        // Not a troop in any gameplay sense — added purely so OwnerPlayerId is resolvable
        // wherever ownership needs to be checked (e.g. DeployProgressIndicatorManager's own
        // deploy-progress ring) — mirrors AoeRootCard/BallisticMissileCard's own TroopComponent.
        ecs.AddComponent(id, new TroopComponent { OwnerPlayerId = ownerPlayerId, IsPhysicalTroop = false });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        float distance = Vector2.Distance(source, destination);
        float speed = Mathf.Max(0.01f, TilesPerSecond);
        int flightTicks = Mathf.Max(1, TickManager.SecondsToTicks(distance / speed));

        // The tornado doesn't actually move until TornadoProjectileSystem sees it as
        // activated (gated on ActivationQuery.IsActivated, which itself respects
        // ActivatableComponent) — see that system's own comment — so its total lifetime is
        // the activation delay PLUS however long the full source -> destination sweep takes.
        int lifetimeTicks = (int)ticksUntilActive + flightTicks;
        ecs.AddComponent(id, new LifetimeComponent
        {
            TicksRemaining        = lifetimeTicks,
            InitialTicksRemaining = lifetimeTicks,
        });

        ecs.AddComponent(id, new TornadoProjectileComponent
        {
            DestinationX   = destination.x,
            DestinationY   = destination.y,
            TilesPerSecond = TilesPerSecond,
            HitRadius      = HitRadius,
            OwnerPlayerId  = ownerPlayerId,
        });
    }
}
