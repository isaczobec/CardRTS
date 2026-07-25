using System.Collections.Generic;
using UnityEngine;

// Spawns an AoeSpellCard-shaped entity (Position/Renderable/Stats/non-physical TroopComponent
// for owner tracking/ActivatableComponent) at the played point — but instead of pulsing
// damage via DamageAuraComponent, it just sits there as a telegraph for RootDelaySeconds
// (LifetimeComponent) while a scheduled call counts down. RootDelaySeconds after the entity
// ACTIVATES (not from the moment it's played — see OnPlayed's own comment on how that's
// computed), ResolveAoeRoot fires: every enemy troop within AoeSpellCard.Range of the cast
// point (the exact same radius AoeSpellCard's own blast uses — deliberately reusing its
// constant rather than declaring a separate one, so the two stay in lockstep by construction)
// gets rooted in place (RootedComponent, ModifierID.Rooted — see RootedSystem) for
// RootDurationSeconds.
public class AoeRootCard : SpawnAtPointCard
{
    private const float ActivationDelaySeconds = 2f;
    private const float MaxDistanceFromBuilding = 25f;
    private const float MaxDistanceFromTroop = 15f;

    // How long after the entity ACTIVATES the root actually lands — see OnPlayed.
    private const float RootDelaySeconds = 4f;
    // How long an affected enemy is rooted in place once the effect lands.
    private const float RootDurationSeconds = 5f;

    // Scratch, reused across every resolve rather than reallocated each time — mirrors
    // AbilityManager's own _queryBuffer.
    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    static AoeRootCard()
    {
        ScheduledCallSystem.RegisterCall(ScheduledCallType.AoeRootResolve, ResolveAoeRoot);
    }

    public override int ShopGoldCost => 100;

    public override CardType Type => CardType.AoeRoot;
    public override string Title => "AOE Root";
    public override string ImageName => "AoeRoot";
    public override string Description => "Marks a point on the ground. After a delay, every enemy troop caught in the area is rooted in place.";
    public override string IndicatorPrefabName => "AoeRoot";

    public override StatsComponent DefaultStats => BuildStats();
    public override ResourceCost Cost => new ResourceCost
        {
            Gems = 4,
            Wood = 80
        };
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;
    public override float MaxDistanceFromFriendlyTroop => MaxDistanceFromTroop;
    public override bool AllowsFriendlyTroopRange() => true;

    // MaxHealth/Speed/Armor/Damage/AttackSpeed/SpellResist are STAT_NA — this entity has no
    // HealthComponent/MovableComponent and deals no direct damage itself; Range is real
    // (AoeSpellCard.Range) purely so the card face shows the actual affected radius.
    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth   = StatsComponent.STAT_NA,
        Speed       = StatsComponent.STAT_NA,
        Range       = AoeSpellCard.Range,
        Armor       = StatsComponent.STAT_NA,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
        SpellResist = StatsComponent.STAT_NA,
    };

    // The indicator prefab's mesh is assumed to be authored at 1-unit diameter (same
    // convention as AoeSpellCard/RangeIndicatorPrefab), so this maps AoeSpellCard.Range (a
    // radius) directly onto a uniform X/Z scale — Y is left alone since it's a flat ground disc.
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
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.AoeRoot });
        ecs.AddComponent(id, BuildStats());

        // Not a troop in any gameplay sense — added purely so OwnerPlayerId is available
        // wherever ownership needs to be resolved (e.g. DeployProgressIndicatorManager
        // showing the deploy-progress disc only to the client whose spell this is). Mirrors
        // AoeSpellCard's own TroopComponent.
        ecs.AddComponent(id, new TroopComponent { OwnerPlayerId = ownerPlayerId, IsPhysicalTroop = false });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        // The telegraph entity itself disappears right around when the root actually lands.
        int lifetimeTicks = TickManager.SecondsToTicks(RootDelaySeconds);
        ecs.AddComponent(id, new LifetimeComponent
        {
            TicksRemaining        = lifetimeTicks,
            InitialTicksRemaining = lifetimeTicks,
        });

        // ActivatableComponent._ticksUntilActive counts down unconditionally, one tick per
        // tick, with nothing able to pause/veto it (see ActivationSystem.Execute) — so
        // activation always lands exactly ActivationDelaySeconds after creation. Scheduling
        // for (ActivationDelaySeconds + RootDelaySeconds) ticks from NOW therefore lands
        // exactly RootDelaySeconds after activation, without needing to actually wait for/
        // react to EntityActivatedEvent — the same "compute the exact tick directly" approach
        // AbilityManager's own windup-then-resolve abilities (Ice Nova, Ground Slam) use.
        //
        // The cast point (Param1/Param2) and caster's owner id (Param0) are captured here
        // rather than re-read off this entity at resolve time, since LifetimeComponent means
        // this entity (and its components) may already be gone by the moment the call fires.
        int totalDelayTicks = TickManager.SecondsToTicks(ActivationDelaySeconds + RootDelaySeconds);
        ScheduledCallSystem.Schedule(ecs, ScheduledCallType.AoeRootResolve, totalDelayTicks,
            param0: ownerPlayerId, param1: x, param2: y);

        return id;
    }

    // The actual root effect — registered against ScheduledCallType.AoeRootResolve in the
    // static constructor above. call.Param0 is the casting player's own id, call.Param1/
    // Param2 the cast point — both captured at cast time (see OnPlayed's own comment on why),
    // not re-read off this card's spawned entity, which may already be gone by now.
    //
    // Creating the Rooted modifiers is harmless to duplicate across the predicting client and
    // the server — same reasoning as AbilityManager's ResolveIceNova/RingOfProjectilesAbility:
    // it acts through ModifierComponent.TargetEntityId, not any entity's own id — so, like
    // those, no isServer guard is needed here.
    private static void ResolveAoeRoot(ECS ecs, ScheduledCallComponent call)
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
                TicksRemaining = TickManager.SecondsToTicks(RootDurationSeconds),
                ModifierID     = ModifierID.Rooted,
            });
            ecs.AddComponent(modifier.Id, new RootedComponent());
            ecs.AddComponent(modifier.Id, new RenderableModifierComponent
            {
                Type = RenderableModifierType.Rooted,
            });
        }
    }
}
