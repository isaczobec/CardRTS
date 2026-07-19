// Marks a building as respawnable: on death it enters a cooldown instead of being deleted.
// CooldownTicks is the authored duration; TicksUntilRespawn counts down from that value.
public struct RespawnableInPlaceComponent : IComponent
{
    public ulong CooldownTicks;
    public ulong TicksUntilRespawn;

    // Whether EntityTimerTextRenderer should show a floating countdown while this entity is
    // on cooldown. A bare/defaulted component has this false — every existing spawn site
    // (see EntitySpawnAction) sets it true explicitly, matching this component's intended
    // default of "shown", the same way e.g. CardPlayRangeMultiplier's intended default of 1
    // is set explicitly at every call site rather than relying on a struct field initializer.
    public bool ShowTimer;

    public bool IsOnCooldown => TicksUntilRespawn > 0;
}
