public class ProjectileActivatedEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public ulong OwnerEntityId { get; set; }
    public ulong TargetEntityId { get; set; }

    // The fire position, captured at the moment this was raised — see PositionQuery.TryGet.
    public float X { get; set; }
    public float Y { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[32];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(OwnerEntityId).CopyTo(data, 8);
        System.BitConverter.GetBytes(TargetEntityId).CopyTo(data, 16);
        System.BitConverter.GetBytes(X).CopyTo(data, 24);
        System.BitConverter.GetBytes(Y).CopyTo(data, 28);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        OwnerEntityId = System.BitConverter.ToUInt64(data, 8);
        TargetEntityId = System.BitConverter.ToUInt64(data, 16);
        X = System.BitConverter.ToSingle(data, 24);
        Y = System.BitConverter.ToSingle(data, 28);
    }

    public override (float X, float Y)? Position => (X, Y);
}
