public class ProjectileActivatedEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public ulong OwnerEntityId { get; set; }
    public ulong TargetEntityId { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[24];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(OwnerEntityId).CopyTo(data, 8);
        System.BitConverter.GetBytes(TargetEntityId).CopyTo(data, 16);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        OwnerEntityId = System.BitConverter.ToUInt64(data, 8);
        TargetEntityId = System.BitConverter.ToUInt64(data, 16);
    }
}
