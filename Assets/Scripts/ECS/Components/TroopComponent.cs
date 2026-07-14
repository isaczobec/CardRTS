public struct TroopComponent : IComponent
{
    public const ushort NEUTRAL_OWNER_PLAYER_ID = ushort.MaxValue;
    public ushort OwnerPlayerId;

    public bool IsDead;
}
