// Mirrors DamageDealtEvent — raised by HealRequest.Execute so a renderer (e.g. floating "+N"
// text) can react to healing the same way it already can to damage.
public class HealDealtEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public int Amount { get; set; }

    // Captured by HealRequest at the moment this was raised — see PositionQuery.TryGet.
    public float X { get; set; }
    public float Y { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[20];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(Amount).CopyTo(data, 8);
        System.BitConverter.GetBytes(X).CopyTo(data, 12);
        System.BitConverter.GetBytes(Y).CopyTo(data, 16);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        Amount = System.BitConverter.ToInt32(data, 8);
        X = System.BitConverter.ToSingle(data, 12);
        Y = System.BitConverter.ToSingle(data, 16);
    }

    public override (float X, float Y)? Position => (X, Y);
}
