public struct TroopComponent : IComponent
{
    public const ushort NEUTRAL_OWNER_PLAYER_ID = ushort.MaxValue;
    public ushort OwnerPlayerId;

    public ulong _ticksUntilActive;
    public bool IsActive => _ticksUntilActive == 0;
    public long TicksUntilActive => (long)_ticksUntilActive;

    public bool IsDead;

    // Single guard for "may this troop currently be interacted with / act": must have
    // finished its activation delay and not be dead. Use this instead of IsActive alone.
    public bool CanTakeActions => IsActive && !IsDead;
}