using System.Collections.Generic;
using System.IO;

public class SetTargetsInput : InputBase
{
    public List<ulong> FriendlyTroopIds = new List<ulong>();
    public List<ulong> TargetTroopIds = new List<ulong>();
    public bool AdditionalSelect;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);

        w.Write(FriendlyTroopIds.Count);
        foreach (ulong id in FriendlyTroopIds)
            w.Write(id);

        w.Write(TargetTroopIds.Count);
        foreach (ulong id in TargetTroopIds)
            w.Write(id);

        w.Write(AdditionalSelect);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);

        int friendlyCount = r.ReadInt32();
        FriendlyTroopIds = new List<ulong>(friendlyCount);
        for (int i = 0; i < friendlyCount; i++)
            FriendlyTroopIds.Add(r.ReadUInt64());

        int targetCount = r.ReadInt32();
        TargetTroopIds = new List<ulong>(targetCount);
        for (int i = 0; i < targetCount; i++)
            TargetTroopIds.Add(r.ReadUInt64());

        AdditionalSelect = r.ReadBoolean();
    }
}
