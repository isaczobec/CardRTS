using System.IO;

// Input for buying an upgrade from the shop and equipping it onto one specific card already
// in the buyer's deck/hand — see BuyUpgradeSystem. Mirrors BuyCardInput's shape, plus the
// target card to attach the upgrade to.
public class BuyUpgradeInput : InputBase
{
    public UpgradeType UpgradeType;
    public ulong TargetCardEntityId;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write((byte)UpgradeType);
        w.Write(TargetCardEntityId);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        UpgradeType = (UpgradeType)r.ReadByte();
        TargetCardEntityId = r.ReadUInt64();
    }
}
