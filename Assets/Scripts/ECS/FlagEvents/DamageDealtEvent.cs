public class DamageDealtEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public ulong DealerEntityId { get; set; }
    public int Amount { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[20];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(DealerEntityId).CopyTo(data, 8);
        System.BitConverter.GetBytes(Amount).CopyTo(data, 16);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        DealerEntityId = System.BitConverter.ToUInt64(data, 8);
        Amount = System.BitConverter.ToInt32(data, 16);
    }
}
