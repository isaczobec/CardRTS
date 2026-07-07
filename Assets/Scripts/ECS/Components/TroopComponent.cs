public struct TroopComponent : IComponent
{
    public const ushort NEUTRAL_OWNER_PLAYER_ID = ushort.MaxValue;
    public ushort OwnerPlayerId;

    public ulong _ticksUntilActive;
    public bool IsActive => _ticksUntilActive == 0;
    public long TicksUntilActive => (long)_ticksUntilActive;
}