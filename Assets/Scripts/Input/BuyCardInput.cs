using System.IO;

// Input for buying a new copy of a card from the shop (see ShopUIManager/BuyCardSystem).
// Unlike SpawnAtPointInput/AbilityUsedInput, this doesn't reference an existing entity —
// there's no card entity yet until BuyCardSystem creates one on the server — so it
// references the card being bought by its CardType instead.
public class BuyCardInput : InputBase
{
    public CardType CardType;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write((byte)CardType);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        CardType = (CardType)r.ReadByte();
    }
}
