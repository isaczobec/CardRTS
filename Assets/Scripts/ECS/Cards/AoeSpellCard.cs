using UnityEngine;

// Simple circular-radius AOE spell — the "single point, single entity" SpawnAtPointCard
// shape fits it as-is, so OnPlayed just needs to create one entity at the played point.
// Every SpawnAtPointCard-spawned entity (troop, building, or this) goes through the same
// activation delay via ActivatableComponent/ActivationSystem, so a client's locally
// predicted spell effect and the server's authoritative one come online in sync instead of
// the effect starting the instant the card is played.
//
// Deals its damage via DamageAuraComponent/DamageAuraSystem (pulses for as long as it's
// active) and expires via LifetimeComponent/LifetimeSystem (deactivated, then deleted on
// the server, once its duration runs out).
public class AoeSpellCard : SpawnAtPointCard
{
    private const float ActivationDelaySeconds = 2f;
    private const float MaxDistanceFromBuilding = 25f;
    private const float MaxDistanceFromTroop = 15f;

    // Blast radius, in world/tile units. Doubles as the placement indicator's diameter
    // (see OnIndicatorSpawned) so the player can see exactly what the blast will cover
    // before committing to it. Public so AbilityManager's AoeSpellCloneAbility can preview
    // the exact same radius (CursorCircleRadius) without duplicating/drifting from it.
    public const int Range = 5;

    // 2.8x the old 20 — see BasicMeleeTroopCard for the rebalance baseline this is scaled from.
    private const int Damage = 56;
    // Pulse interval, once active — same units as every other card's AttackSpeed
    // (milliseconds authored here, converted to ticks below).
    private const float AttackSpeedMilliseconds = 1000f;

    // How long the aura keeps pulsing after it activates, before LifetimeSystem expires it.
    private const float DurationSeconds = 5f;

    public override int ShopGoldCost => 100; 


    public override CardType Type => CardType.AoeSpell;
    public override string Title => "AOE Spell";
    public override string ImageName => "AoeSpell";
    public override string Description => "A circular blast at the targeted point.";
    public override string IndicatorPrefabName => "AoeSpell";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Gems = 5,
            Metal = 80
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;
    public override bool AllowsFriendlyTroopRange() => true;

    // MaxHealth/Speed/Armor/SpellResist are STAT_NA — this entity has no HealthComponent/
    // MovableComponent, so nothing ever reads them. Range/Damage/AttackSpeed are real:
    // DamageAuraSystem reads them straight off this StatsComponent via StatsQuery, which
    // only falls back to a default when there's no StatsComponent at all — leaving these
    // at STAT_NA (int.MinValue) would have it deal int.MinValue damage on every tick.
    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = Range,
        Armor       = StatsComponent.STAT_NA,
        Damage      = Damage,
        AttackSpeed = TickManager.MillisecondsToTicks(AttackSpeedMilliseconds),
        SpellResist = StatsComponent.STAT_NA,
    };

    // The indicator prefab's mesh is assumed to be authored at 1-unit diameter (same
    // convention as RangeIndicatorPrefab), so this maps Range (a radius) directly onto a
    // uniform X/Z scale — Y is left alone since it's a flat ground disc.
    public override void OnIndicatorSpawned(GameObject indicator)
    {
        float diameter = Range * 2f;
        indicator.transform.localScale = new Vector3(diameter, 1f, diameter);
    }

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.AoeSpell });
        ecs.AddComponent(id, BuildStats());

        // Not a troop in any gameplay sense (no HealthComponent/SelectableComponent, so
        // nothing that keys off those ever notices it) — added purely so OwnerPlayerId is
        // available wherever ownership needs to be resolved, e.g.
        // DeployProgressIndicatorManager showing the deploy-progress disc only to the
        // client whose spell this is. Same reuse BuildingSpawnHelper already relies on for
        // buildings.
        ecs.AddComponent(id, new TroopComponent { OwnerPlayerId = ownerPlayerId, IsPhysicalTroop = false });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        ecs.AddComponent(id, new DamageAuraComponent
        {
            RangeMultiplier       = 1f,
            AttackSpeedMultiplier = 1f,
            DamageType            = DamageType.Spell,
        });

        int lifetimeTicks = TickManager.SecondsToTicks(DurationSeconds);
        ecs.AddComponent(id, new LifetimeComponent
        {
            TicksRemaining        = lifetimeTicks,
            InitialTicksRemaining = lifetimeTicks,
        });
    }
}
