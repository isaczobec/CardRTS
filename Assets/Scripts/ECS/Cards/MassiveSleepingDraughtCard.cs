using System.Collections.Generic;
using UnityEngine;

// Exactly AoeRootCard's own shape (same telegraph entity, same ActivationDelaySeconds, same
// delay-then-resolve timing, same enemy-troops-in-AoeSpellCard.Range scan) — just applies
// SleepingDraughtCard's own sleeping effect (SilenceComponent + a StatModifierComponent slow
// + RemoveModifierOnDamageComponent, reusing ModifierID.SleepingDraught/
// RenderableModifierType.SleepingDraught) to every enemy troop caught in the area instead of
// RootedComponent, with a more powerful slow than the single-target version's own -30%.
public class MassiveSleepingDraughtCard : SpawnAtPointCard
{
    private const float ActivationDelaySeconds = 2f;
    private const float MaxDistanceFromBuilding = 25f;
    private const float MaxDistanceFromTroop = 15f;
    private const int Range = 16;


    // How long after the entity ACTIVATES the sleep actually lands — mirrors AoeRootCard's
    // own RootDelaySeconds exactly ("exactly like the AOE Root" design ask).
    private const float SleepDelaySeconds = 4f;
    // How long an affected enemy sleeps once the effect lands — mirrors AoeRootCard's own
    // RootDurationSeconds.
    private const float SleepDurationSeconds = 5f;
    // Stronger than SleepingDraughtCard's own -30% — explicit "more powerful slow" design ask.
    private const float SlowRatio = -0.5f;

    // Scratch, reused across every resolve rather than reallocated each time — mirrors
    // AoeRootCard's own _queryBuffer.
    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    static MassiveSleepingDraughtCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.MassiveSleepingDraughtResolve, ResolveMassiveSleepingDraught);
    }

    public override int ShopGoldCost => 160;

    public override CardType Type => CardType.MassiveSleepingDraught;
    public override CardCategory Category => CardCategory.Spell;
    public override string Title => "Massive Sleeping Draught";
    public override string ImageName => "MassiveSleepingDraught";
    public override string Description => "Marks a point on the ground. After a delay, every enemy troop caught in the area is put to sleep: silenced and 50% slowed. Wakes up early if it takes any damage.";
    public override string IndicatorPrefabName => "MassiveSleepingDraught";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Gems = 8,
            Wood = 100,
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;
    public override bool AllowsFriendlyTroopRange() => true;

    // MaxHealth/Speed/Armor/Damage/AttackSpeed/SpellResist are STAT_NA — this entity has no
    // HealthComponent/MovableComponent and deals no direct damage itself; Range is real
    // (AoeSpellCard.Range) purely so the card face shows the actual affected radius — mirrors
    // AoeRootCard's own BuildStats exactly.
    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = Range,
        Armor       = StatsComponent.STAT_NA,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
        SpellResist = StatsComponent.STAT_NA,
    };

    // The indicator prefab's mesh is assumed to be authored at 1-unit diameter — mirrors
    // AoeRootCard/AoeSpellCard's own convention.
    public override void OnIndicatorSpawned(GameObject indicator)
    {
        float diameter = AoeSpellCard.Range * 2f;
        indicator.transform.localScale = new Vector3(diameter, 1f, diameter);
    }

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.MassiveSleepingDraught });
        ecs.AddComponent(id, BuildStats());

        // Not a troop in any gameplay sense — added purely so OwnerPlayerId is available
        // wherever ownership needs to be resolved — mirrors AoeRootCard's own TroopComponent.
        ecs.AddComponent(id, new TroopComponent { OwnerPlayerId = ownerPlayerId, IsPhysicalTroop = false });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        // The telegraph entity itself disappears right around when the sleep actually lands.
        int lifetimeTicks = TickManager.SecondsToTicks(SleepDelaySeconds);
        ecs.AddComponent(id, new LifetimeComponent
        {
            TicksRemaining        = lifetimeTicks,
            InitialTicksRemaining = lifetimeTicks,
        });

        // Same "compute the exact tick directly" approach AoeRootCard's own OnPlayed uses —
        // see its comment for why (ActivatableComponent's own countdown always lands exactly
        // ActivationDelaySeconds after creation, with nothing able to pause/veto it).
        int totalDelayTicks = TickManager.SecondsToTicks(ActivationDelaySeconds + SleepDelaySeconds);
        ScheduledCallSystem.Schedule(ecs, ScheduledCallType.MassiveSleepingDraughtResolve, totalDelayTicks,
            param0: ownerPlayerId, param1: x, param2: y);

        return id;
    }

    // The actual sleep effect — registered against ScheduledCallType.MassiveSleepingDraughtResolve
    // in the static constructor above. call.Param0 is the casting player's own id, call.Param1/
    // Param2 the cast point — both captured at cast time, not re-read off this card's spawned
    // entity, which may already be gone by now (mirrors AoeRootCard.ResolveAoeRoot exactly).
    //
    // Creating the sleep modifiers is harmless to duplicate across the predicting client and
    // the server — same reasoning as AoeRootCard.ResolveAoeRoot: it acts through
    // ModifierComponent.TargetEntityId, not any entity's own id — so no isServer guard is
    // needed here either.
    private static void ResolveMassiveSleepingDraught(ECS ecs, ScheduledCallComponent call)
    {
        ushort casterOwnerId = (ushort)call.Param0;
        float x = call.Param1;
        float y = call.Param2;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null) return;

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(x, y, AoeSpellCard.Range, _queryBuffer);

        foreach (ulong targetId in _queryBuffer)
        {
            if (!troopStore.HasComponent(targetId)) continue;
            if (troopStore.GetComponent(targetId).OwnerPlayerId == casterOwnerId) continue;
            if (!ActivationQuery.IsActivated(ecs, targetId)) continue;

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = targetId,
                TicksRemaining = TickManager.SecondsToTicks(SleepDurationSeconds),
                ModifierID     = ModifierID.SleepingDraught,
            });
            ecs.AddComponent(modifier.Id, new SilenceComponent());
            ecs.AddComponent(modifier.Id, new StatModifierComponent
            {
                SpeedRatioBonus = SlowRatio,
            });
            ecs.AddComponent(modifier.Id, new RemoveModifierOnDamageComponent());
            ecs.AddComponent(modifier.Id, new RenderableModifierComponent
            {
                Type = RenderableModifierType.SleepingDraught,
            });
        }
    }
}
