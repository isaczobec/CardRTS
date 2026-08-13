using System.IO;

// Sent once by RecallInputManager after the player holds G while hovering near a friendly
// troop/building for RecallInputManager's own hold-threshold — begins an 8-second recall
// channel for EntityId (see RecallSystem). Re-sending this for an entity that's already
// recalling is harmless (RecallSystem.BeginRecall is idempotent).
public class RecallInput : InputBase
{
    public ulong EntityId;

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(EntityId);
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r = new BinaryReader(ms);
        EntityId = r.ReadUInt64();
    }
}
