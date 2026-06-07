public class ComponentAddedEvent<T> : FlagEvent where T : IComponent
{
    public override byte[] Serialize() => System.Array.Empty<byte>();
    public override void Deserialize(byte[] data) { }
}
