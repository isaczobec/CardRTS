using System.IO;

// Input for SpawnAtPointCard-kind cards (see Card.cs) — plays a card at a single world
// point. Other card kinds (multi-point, entity-targeted) get their own InputBase subtypes
// alongside this one rather than overloading this shape; see SpawnAtPointCardPlaySystem.
public class SpawnAtPointInput : InputBase
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
