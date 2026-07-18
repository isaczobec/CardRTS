using System.Collections.Generic;
using System.IO;

// Sets AIModeComponent.Mode to Mode for every entity in EntityIds that has one and is owned
// by the requesting client — see SetAIModeSystem. Mirrors SetTargetsInput's shape (a mode
// plus a list of entity ids), sent once per hotkey press (S/D/F — see AIModeUI) for the
// current selection.
public class SetAIModeInput : InputBase
{
    public AIMode Mode;
    public List<ulong> EntityIds = new List<ulong>();

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);

        w.Write((byte)Mode);
        w.Write(EntityIds.Count);
        foreach (ulong id in EntityIds)
            w.Write(id);

        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);

        Mode = (AIMode)r.ReadByte();

        int count = r.ReadInt32();
        EntityIds = new List<ulong>(count);
        for (int i = 0; i < count; i++)
            EntityIds.Add(r.ReadUInt64());
    }
}
