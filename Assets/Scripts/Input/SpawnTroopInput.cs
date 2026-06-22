using System.IO;

public class SpawnTroopInput : InputBase
{
    public float X;
    public float Y;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(X);
        w.Write(Y);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        X = r.ReadSingle();
        Y = r.ReadSingle();
    }
}
