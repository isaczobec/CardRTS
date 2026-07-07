using System.Collections.Generic;
using System.IO;

public class MoveTroopInput : InputBase
{
    public struct EntityDestination
    {
        public ulong EntityId;
        public float DestinationX;
        public float DestinationY;
    }

    public List<EntityDestination> Moves = new List<EntityDestination>();

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(Moves.Count);
        foreach (EntityDestination move in Moves)
        {
            w.Write(move.EntityId);
            w.Write(move.DestinationX);
            w.Write(move.DestinationY);
        }
        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);
        int count = r.ReadInt32();
        Moves = new List<EntityDestination>(count);
        for (int i = 0; i < count; i++)
        {
            Moves.Add(new EntityDestination
            {
                EntityId      = r.ReadUInt64(),
                DestinationX  = r.ReadSingle(),
                DestinationY  = r.ReadSingle(),
            });
        }
    }
}
