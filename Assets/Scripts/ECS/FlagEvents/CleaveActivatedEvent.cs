// Raised whenever CleaveSystem's splash effect actually triggers (see CleaveUpgrade) — once
// per qualifying original hit that carries a positive splash amount, not once per individual
// splash target it ends up hitting. Effect-spawners (see FlagEventEffectManager) can key off
// this to play a cleave VFX/SFX at the point of impact.
public class CleaveActivatedEvent : FlagEvent
{
    // The troop whose Cleave upgrade triggered.
    public ulong EntityId { get; set; }

    // Where the original hit that triggered this landed — captured at the moment this was
    // raised, same convention as AttackWindupBeganEvent's own X/Y.
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
