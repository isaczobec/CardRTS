using System.IO;

public class CardPlayedInput : InputBase
{
    public ulong CardEntityId;
    public float X;
    public float Y;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(CardEntityId);
        w.Write(X);
        w.Write(Y);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        CardEntityId = r.ReadUInt64();
        X = r.ReadSingle();
        Y = r.ReadSingle();
    }
}
