using System;

public interface IComponentStore
{
    uint Size { get; }
    Type Type { get; }

    bool HasComponent(ulong entityId);
    void ForEach(Action<ulong> perIdFunction);
    uint IdToIndex(ulong entityId);
    ulong IndexToId(uint index);
}
