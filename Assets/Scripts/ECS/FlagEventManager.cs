using System;
using System.IO;

public class FlagEventManager
{
    private readonly EventDispatcher<FlagEvent> _dispatcher = new();

    public void Subscribe<T>(Action<T> callback) where T : FlagEvent
        => _dispatcher.Subscribe(callback);

    public void Unsubscribe<T>(Action<T> callback) where T : FlagEvent
        => _dispatcher.Unsubscribe(callback);

    public void Add<T>(T evt) where T : FlagEvent => _dispatcher.Raise(evt);

    public void Flush() => _dispatcher.Flush();

    // Serializes all pending networkable events. Call before Flush so the list is still populated.
    // Format: [count:int32][typeId:ushort][dataLen:ushort][data:bytes]...
    public byte[] SerializePending(TypeRegistry<FlagEvent> registry)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var pending = _dispatcher.Pending;
        int networkableCount = 0;
        foreach (var evt in pending)
            if (evt.ShouldNetwork()) networkableCount++;

        writer.Write(networkableCount);
        foreach (var evt in pending)
        {
            if (!evt.ShouldNetwork()) continue;
            ushort typeId = registry.GetIDForType(evt.GetType());
            byte[] data = evt.Serialize();
            writer.Write(typeId);
            writer.Write((ushort)data.Length);
            writer.Write(data);
        }

        return ms.ToArray();
    }

    // Deserializes events from bytes and adds them to the pending queue. Call Flush() afterwards to dispatch.
    public void AddFromBytes(byte[] data, TypeRegistry<FlagEvent> registry)
    {
        if (data == null || data.Length == 0) return;
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ushort typeId = reader.ReadUInt16();
            ushort dataLen = reader.ReadUInt16();
            byte[] eventData = reader.ReadBytes(dataLen);

            Type eventType = registry.GetTypeForID(typeId);
            FlagEvent evt = (FlagEvent)Activator.CreateInstance(eventType);
            evt.Deserialize(eventData);
            _dispatcher.Raise(evt);
        }
    }
}
