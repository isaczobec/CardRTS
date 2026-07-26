public class EntityActivatedEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    // Captured by ActivationSystem at the moment this was raised — see PositionQuery.TryGet.
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
