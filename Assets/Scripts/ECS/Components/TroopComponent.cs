public struct TroopComponent : IComponent
{
    public ushort OwnerPlayerId;

    public ulong tickToBecomeActive;
    public bool IsActive(ulong currentTick) => tickToBecomeActive <= currentTick;
    public long TicksUntilActive(ulong currentTick) => (long)(tickToBecomeActive - currentTick);
}