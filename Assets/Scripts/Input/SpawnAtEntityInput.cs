using System.IO;

// Input for TargetEntityCard-kind cards (see Card.cs) — plays a card on an existing
// selectable entity instead of a ground point. See TargetEntityCardPlaySystem.
public class SpawnAtEntityInput : InputBase
{
    public ulong CardEntityId;
    public ulong TargetEntityId;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(CardEntityId);
        w.Write(TargetEntityId);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        CardEntityId = r.ReadUInt64();
        TargetEntityId = r.ReadUInt64();
    }
}
