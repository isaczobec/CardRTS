// Innate troop trait — attach directly to a troop entity (not via a separate modifier
// entity; mirrors OnKillScheduleComponent's own "plain, always-on component" shape) to make
// OnHitScheduleSystem schedule CallType (see ScheduledCallSystem) DelayTicks ticks after this
// troop lands a hit on anything — every hit that actually reaches DamageRequest.Execute,
// unlike OnKillScheduleComponent, which only fires on a killing blow.
public struct OnHitScheduleComponent : IComponent
{
    public ScheduledCallType CallType;
    public int DelayTicks;
}
