using System.IO;

// Input for an AbilityType.TargetEntity ability (see Ability/AbilitySystem) — same idea as
// AbilityUsedAtLocationInput, but targets an existing selectable entity instead of a world
// point. AbilitySystem rejects this if the target doesn't exist/isn't currently selectable,
// doesn't match the ability's CanTargetFriendly/CanTargetEnemyOrNeutral flags, or is
// further than the ability's own Range from the casting entity.
public class AbilityUsedOnEntityInput : InputBase
{
    public int AbilityId;
    public ulong CastingEntityId;
    public ulong TargetEntityId;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(AbilityId);
        w.Write(CastingEntityId);
        w.Write(TargetEntityId);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        AbilityId = r.ReadInt32();
        CastingEntityId = r.ReadUInt64();
        TargetEntityId = r.ReadUInt64();
    }
}
