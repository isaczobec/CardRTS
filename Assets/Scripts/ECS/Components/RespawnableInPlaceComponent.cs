// Marks a building as respawnable: on death it enters a cooldown instead of being deleted.
// CooldownTicks is the authored duration; TicksUntilRespawn counts down from that value.
public struct RespawnableInPlaceComponent : IComponent
{
    public ulong CooldownTicks;
    public ulong TicksUntilRespawn;

    public bool IsOnCooldown => TicksUntilRespawn > 0;
}
