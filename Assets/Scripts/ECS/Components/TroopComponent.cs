public struct TroopComponent : IComponent
{
    public const ushort NEUTRAL_OWNER_PLAYER_ID = ushort.MaxValue;
    public ushort OwnerPlayerId;

    public ulong tickToBecomeActive;
    public bool IsActive(ulong currentTick) => tickToBecomeActive <= currentTick;
    public long TicksUntilActive(ulong currentTick) => (long)(tickToBecomeActive - currentTick);
}