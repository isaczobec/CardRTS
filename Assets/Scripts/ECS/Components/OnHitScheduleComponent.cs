// Innate troop trait — attach directly to a troop entity (not via a separate modifier
// entity; mirrors OnKillScheduleComponent's own "plain, always-on component" shape) to make
// OnHitScheduleSystem schedule CallType (see ScheduledCallSystem) DelayTicks ticks after this
// troop lands a qualifying hit — by default every hit that actually reaches
// DamageRequest.Execute, unlike OnKillScheduleComponent, which only fires on a killing blow.
public struct OnHitScheduleComponent : IComponent
{
    public ScheduledCallType CallType;
    public int DelayTicks;

    // Qualifying hits needed between each proc. 0 (the struct default) is treated as 1 by
    // OnHitScheduleSystem, i.e. procs on every qualifying hit — this component's original
    // behavior before this field existed (see StalkerCard's ambush, which never sets this).
    public int PeriodHits;

    // Running countdown to the next proc — lives on the component (not a system-instance
    // field) so it survives a client-prediction reconciliation rewind correctly, matching
    // PeriodicDamageReductionComponent.HitsTaken's own reasoning. 0 (the struct default)
    // procs on the very next qualifying hit; a card that wants its FIRST proc to also wait
    // out a full PeriodHits (rather than firing early on hit 1) should initialize this to
    // PeriodHits itself at spawn time — see SantaClausCard.
    public int HitsUntilProc;

    // Whether only a hit against an enemy PHYSICAL troop counts toward PeriodHits at all —
    // a hit against a building (or anything else with a TroopComponent that isn't a real
    // troop) is ignored entirely, not even decrementing HitsUntilProc, when this is true.
    // Defaults to false (every hit counts, matching this component's original behavior).
    public bool RequireEnemyTroopHit;
}
