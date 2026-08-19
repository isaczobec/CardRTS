using System.IO;

// Input for selling a card back from hand — see CardHandRenderer's double-right-click
// gesture and RefundCardSystem. Unlike SpawnAtPointInput, there's no play point/target — just
// which card entity to refund.
public class RefundCardInput : InputBase
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
