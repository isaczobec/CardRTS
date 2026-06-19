using System;

public interface IComponentStore
{
    uint Size { get; }
    Type Type { get; }

    bool HasComponent(ulong entityId);
    void ForEach(Action<ulong> perIdFunction);
    uint IdToIndex(ulong entityId);
    ulong IndexToId(uint index);

    byte[] GetComponentData(ulong entityId);
    void ApplyComponentData(ulong entityId, byte[] data);
    void RemoveComponent(ulong entityId);
    void Clear();
}
