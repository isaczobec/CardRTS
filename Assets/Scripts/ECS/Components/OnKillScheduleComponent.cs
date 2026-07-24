// Innate troop trait — attach directly to a troop entity (not via a separate modifier
// entity; mirrors OnDeathResourceDropComponent's own "plain, always-on component" shape,
// since this describes something intrinsic to the troop rather than a temporary externally
// applied effect) to make OnKillScheduleSystem schedule CallType (see ScheduledCallSystem)
// DelayTicks ticks after this troop lands a killing blow on an enemy.
public struct OnKillScheduleComponent : IComponent
{
    public ScheduledCallType CallType;
    public int DelayTicks;

    // Whether killing a BUILDING (not just a real troop) also counts as a qualifying kill.
    // Defaults to false (the struct default) — most on-kill effects only care about troop
    // kills.
    public bool AllowBuildingKills;
}
