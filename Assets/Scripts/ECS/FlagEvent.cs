public abstract class FlagEvent
{
    public abstract byte[] Serialize();
    public abstract void Deserialize(byte[] data);
    public virtual bool ShouldNetwork() { return true; }
}
