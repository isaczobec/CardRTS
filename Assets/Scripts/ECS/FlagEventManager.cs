using System;
using System.Collections.Generic;
using System.IO;

public class FlagEventManager
{
    private readonly EventDispatcher<FlagEvent> _dispatcher = new();

    // Maps the caller's Action to the Action<T> wrapper passed to the dispatcher,
    // so Unsubscribe can look it up without the caller needing to know about it.
    private readonly Dictionary<Action, Delegate> _wrapperMap = new();

    public void Subscribe<T>(Action callback) where T : FlagEvent
    {
        Action<T> wrapper = _ => callback();
        _wrapperMap[callback] = wrapper;
        _dispatcher.Subscribe<T>(wrapper);
    }

    public void Unsubscribe<T>(Action callback) where T : FlagEvent
    {
        if (!_wrapperMap.TryGetValue(callback, out var wrapper)) return;
        _dispatcher.Unsubscribe<T>((Action<T>)wrapper);
        _wrapperMap.Remove(callback);
    }

    public void Add<T>() where T : FlagEvent, new() => _dispatcher.Raise(new T());

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
