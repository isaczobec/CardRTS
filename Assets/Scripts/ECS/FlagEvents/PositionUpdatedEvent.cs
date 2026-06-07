public class PositionUpdatedEvent : FlagEvent
{
    public override byte[] Serialize() => System.Array.Empty<byte>();
    public override void Deserialize(byte[] data) { }
}
