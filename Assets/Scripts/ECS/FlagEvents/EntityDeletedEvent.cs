public class EntityDeletedEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    public override byte[] Serialize() => System.BitConverter.GetBytes(EntityId);
    public override void Deserialize(byte[] data) => EntityId = System.BitConverter.ToUInt64(data, 0);
}
