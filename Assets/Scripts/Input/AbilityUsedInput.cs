using System.IO;

// Input for an AbilityType.Instant ability (see Ability/AbilitySystem) — no location, just
// which ability and who's casting it.
public class AbilityUsedInput : InputBase
{
    public int AbilityId;
    public ulong CastingEntityId;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(AbilityId);
        w.Write(CastingEntityId);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        AbilityId = r.ReadInt32();
        CastingEntityId = r.ReadUInt64();
    }
}
