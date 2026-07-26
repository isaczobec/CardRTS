public class TroopBeginAttackEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    // 0 = no target (shouldn't normally happen — BasicMeleeAISystem always has an
    // active target when it fires this — but renderers should treat 0 as "none").
    public ulong TargetEntityId { get; set; }

    // The attacker's own position, captured at the moment this was raised — see
    // PositionQuery.TryGet.
    public float X { get; set; }
    public float Y { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[24];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(TargetEntityId).CopyTo(data, 8);
        System.BitConverter.GetBytes(X).CopyTo(data, 16);
        System.BitConverter.GetBytes(Y).CopyTo(data, 20);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        TargetEntityId = System.BitConverter.ToUInt64(data, 8);
        X = System.BitConverter.ToSingle(data, 16);
        Y = System.BitConverter.ToSingle(data, 20);
    }

    public override (float X, float Y)? Position => (X, Y);
}
