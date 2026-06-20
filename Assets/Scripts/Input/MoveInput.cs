using System.IO;

public class MoveInput : InputBase
{
    public float DirX;
    public float DirY;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(DirX);
        writer.Write(DirY);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var reader = new BinaryReader(ms);
        DirX = reader.ReadSingle();
        DirY = reader.ReadSingle();
    }
}
