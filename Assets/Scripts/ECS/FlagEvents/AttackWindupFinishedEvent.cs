// Raised by BasicMeleeAISystem/BasicRangedAISystem/TurretAISystem.ResolveAttack once an
// attack windup's ticks reach 0 and it resolves — fires unconditionally, whether the
// hit/shot actually landed (target still valid, in range, CanPerform) or whiffed, but NOT
// when the windup is interrupted early via CancelAttack (a fresh move order cancelling it
// outright isn't "finishing"). See TroopBeginAttackEvent for the windup's start instead.
public class AttackWindupFinishedEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    // The attacker's own position, captured at the moment this was raised — see
    // PositionQuery.TryGet.
    public float X { get; set; }
    public float Y { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[16];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(X).CopyTo(data, 8);
        System.BitConverter.GetBytes(Y).CopyTo(data, 12);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        X = System.BitConverter.ToSingle(data, 8);
        Y = System.BitConverter.ToSingle(data, 12);
    }

    public override (float X, float Y)? Position => (X, Y);
}
