using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Input for MultiPointCard-kind cards (see Card.cs) — plays a card at PointCount ground
// points, all captured client-side (see CardHandRenderer) before this is sent.
public class MultiPointInput : InputBase
{
    public ulong CardEntityId;
    public List<Vector2> Points = new List<Vector2>();

    public override byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);

        w.Write(CardEntityId);
        w.Write(Points.Count);
        foreach (Vector2 point in Points)
        {
            w.Write(point.x);
            w.Write(point.y);
        }

        return ms.ToArray();
    }

    public override void Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var r  = new BinaryReader(ms);

        CardEntityId = r.ReadUInt64();

        int count = r.ReadInt32();
        Points = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
            Points.Add(new Vector2(r.ReadSingle(), r.ReadSingle()));
    }
}
