using System.IO;

// Input for an AbilityType.TargetLocation ability (see Ability/AbilitySystem) — same idea
// as AbilityUsedInput, plus the world point to cast at. AbilitySystem rejects this if (X, Y)
// is further than the ability's own Range from the casting entity.
public class AbilityUsedAtLocationInput : InputBase
{
    public int AbilityId;
    public ulong CastingEntityId;
    public float X;
    public float Y;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(AbilityId);
        w.Write(CastingEntityId);
        w.Write(X);
        w.Write(Y);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        AbilityId = r.ReadInt32();
        CastingEntityId = r.ReadUInt64();
        X = r.ReadSingle();
        Y = r.ReadSingle();
    }
}
