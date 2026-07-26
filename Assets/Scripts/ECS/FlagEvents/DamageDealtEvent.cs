public class DamageDealtEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public ulong DealerEntityId { get; set; }
    public int Amount { get; set; }

    // Captured by whoever raises this (DamageRequest, BruiserSystem's own direct-to-health
    // drain) at the moment it's raised — see PositionQuery.TryGet.
    public float X { get; set; }
    public float Y { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[28];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(DealerEntityId).CopyTo(data, 8);
        System.BitConverter.GetBytes(Amount).CopyTo(data, 16);
        System.BitConverter.GetBytes(X).CopyTo(data, 20);
        System.BitConverter.GetBytes(Y).CopyTo(data, 24);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        DealerEntityId = System.BitConverter.ToUInt64(data, 8);
        Amount = System.BitConverter.ToInt32(data, 16);
        X = System.BitConverter.ToSingle(data, 20);
        Y = System.BitConverter.ToSingle(data, 24);
    }

    public override (float X, float Y)? Position => (X, Y);
}
