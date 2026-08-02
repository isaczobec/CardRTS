using System.IO;

// Input for discarding a card from hand back to the bottom of its owner's deck (see
// CardHandRenderer's double-right-click gesture and DiscardCardSystem). Unlike
// SpawnAtPointInput, there's no play point/target — just which card entity to discard.
public class DiscardCardInput : InputBase
{
    public ulong CardEntityId;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(CardEntityId);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        CardEntityId = r.ReadUInt64();
    }
}
