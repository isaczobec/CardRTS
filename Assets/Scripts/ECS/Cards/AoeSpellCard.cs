using UnityEngine;

// Simple circular-radius AOE spell — the "single point, single entity" SpawnAtPointCard
// shape fits it as-is, so OnPlayed just needs to create one entity at the played point.
// Every SpawnAtPointCard-spawned entity (troop, building, or this) goes through the same
// activation delay via ActivatableComponent/ActivationSystem, so a client's locally
// predicted spell effect and the server's authoritative one come online in sync instead of
// the effect starting the instant the card is played.
//
// The actual AOE effect (damage-over-time, whatever it ends up being) is a separate
// component to be added later — for now this just spawns the entity shell every
// activatable thing needs: ActivatableComponent, RenderableComponent, StatsComponent.
public class AoeSpellCard : SpawnAtPointCard
{
    private const int GoldCost = 4;
    private const float ActivationDelaySeconds = 2f;
    private const float MaxDistanceFromBuilding = 25f;

    // Blast radius, in world/tile units. Doubles as the placement indicator's diameter
    // (see OnIndicatorSpawned) so the player can see exactly what the blast will cover
    // before committing to it.
    private const int Range = 5;

    public override CardType Type => CardType.AoeSpell;
    public override string Title => "AOE Spell";
    public override string ImageName => "AoeSpell";
    public override string Description => "A circular blast at the targeted point.";
    public override string IndicatorPrefabName => "AoeSpell";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost { Gold = GoldCost };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    // MaxHealth/Speed/Armor/Damage/AttackSpeed are STAT_NA for now — none of those apply
    // until the actual spell-effect component exists to give them real values. Range
    // already means something (the blast radius), so it's real from the start.
    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = Range,
        Armor       = StatsComponent.STAT_NA,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
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

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });
    }
}
