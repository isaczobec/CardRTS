public struct TroopComponent : IComponent
{
    public const ushort NEUTRAL_OWNER_PLAYER_ID = ushort.MaxValue;
    public ushort OwnerPlayerId;

    public ulong _ticksUntilActive;
    // Snapshot of _ticksUntilActive at spawn time, set once and never decremented — lets
    // anything showing deploy progress (e.g. DeployProgressIndicatorManager) compute a
    // 1→0 ratio without needing to know the card's activation delay itself.
    public ulong InitialTicksUntilActive;
    public bool IsActive => _ticksUntilActive == 0;
    public long TicksUntilActive => (long)_ticksUntilActive;

    public bool IsDead;

    // Single guard for "may this troop currently be interacted with / act": must have
    // finished its activation delay and not be dead. Use this instead of IsActive alone.
    public bool CanTakeActions => IsActive && !IsDead;
}