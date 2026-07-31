using System.Collections.Generic;
using UnityEngine;

// Two-point spell: click a source point, then a destination point. Once its own deploy
// delay finishes (ActivatableComponent — same as every other SpawnAtPointCard/MultiPointCard-
// spawned entity), every friendly physical troop within Range of the source point is
// teleported to the destination (see BlinkComponent/BlinkSystem/TeleportingModifierComponent/
// TeleportingModifierSystem). Buildings are never teleported (BlinkSystem filters by
// TroopComponent.IsPhysicalTroop).
public class BlinkCard : MultiPointCard
{
    private const float ActivationDelaySeconds = 1f;
    private const float MaxDistanceFromBuilding = 25f;
    private const float MaxDistanceFromTroop = 15f;

    // How far from the source point a friendly troop must be to get teleported. Public so
    // the indicator (both range circles) previews the exact same radius.
    public const int Range = 5;

    // How far apart the two points may be — i.e. the max distance a troop can blink.
    // Enforced client-side (clamped, both visually and on the point actually captured — see
    // MultiPointCard.ClampToPreviousPoint) and re-checked server-side (MultiPointCardPlaySystem).
    private const float MaxBlinkDistance = 20f;

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.Blink;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Blink";
    public override string ImageName => "Blink";
    public override string Description => "Teleports friendly troops near the first point to the second point.";
    public override string IndicatorPrefabName => "Blink";

    public override int PointCount => 2;
    public override float MaxRangeFromPreviousPoint => MaxBlinkDistance;

    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = Range,
        Armor       = StatsComponent.STAT_NA,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
        SpellResist = StatsComponent.STAT_NA,
    };

    public override ResourceCost Cost => new ResourceCost
        {
            Gems = 10
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;
    public override bool AllowsFriendlyTroopRange() => true;

    // Same "1-unit-diameter mesh" convention as AoeSpellCard.OnIndicatorSpawned. Both of
    // Blink's points use the same range-circle prefab, scaled identically: the source
    // circle previews exactly which troops get picked up, the destination circle previews
    // how much room they'll land in.
    public override void OnIndicatorSpawned(GameObject indicator, int pointIndex)
    {
        float diameter = Range * 2f;
        indicator.transform.localScale = new Vector3(diameter, 1f, diameter);
    }

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, IReadOnlyList<Vector2> points)
    {
        Vector2 source = points[0];
        Vector2 destination = points[1];

        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(source.x, source.y));

        // Not a troop in any gameplay sense — added purely so OwnerPlayerId is available to
        // BlinkSystem when it grants teleport modifiers, same reuse AoeSpellCard/
        // BuildingSpawnHelper already rely on.
        ecs.AddComponent(id, new TroopComponent { OwnerPlayerId = ownerPlayerId, IsPhysicalTroop = false });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        ecs.AddComponent(id, new BlinkComponent
        {
            DestinationX = destination.x,
            DestinationY = destination.y,
            Range        = Range,
        });
    }
}
