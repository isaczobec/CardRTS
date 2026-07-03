using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class ComponentStore<T> : IComponentStore where T : struct, IComponent
{
    private CopyBackArray<T> _components;
    private Dictionary<ulong, uint> _idsToComponents;
    private Dictionary<uint, ulong> _componentsToIds;
    private ECS _ecs;

    public uint Size => _components.Size;
    public Type Type => typeof(T);

    public ComponentStore()
    {
        _components = new CopyBackArray<T>(ECS.ENTITIES_CAPACITY);
        _idsToComponents = new Dictionary<ulong, uint>();
        _componentsToIds = new Dictionary<uint, ulong>();
    }

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
    }

    public void AddComponent(ulong id, T component = default)
    {
        CopyBackArray<T>.AddResult res = _components.Add(component);
        _componentsToIds[res.addedIndex] = id;
        _idsToComponents[id] = res.addedIndex;
    }

    public void RemoveComponent(ulong id)
    {
        uint componentIndex = _idsToComponents[id];
        CopyBackArray<T>.RemovalResult res = _components.Remove(componentIndex);

        _idsToComponents.Remove(id);
        _componentsToIds.Remove(res.removedIndex);

        if (res.movedValid)
        {
            ulong movedId = _componentsToIds[res.movedIndex];
            _componentsToIds.Remove(res.movedIndex);
            _componentsToIds[res.removedIndex] = movedId;
            _idsToComponents[movedId] = res.removedIndex;
        }
    }
    

    public bool HasComponent(ulong entityId) => _idsToComponents.ContainsKey(entityId);

    public ref T GetComponent(ulong entityId) => ref _components.GetRef(_idsToComponents[entityId]);

    public ref T GetComponentByIndex(uint index) => ref _components.GetRef(index);

    public void ForEach(SingleComponentSystemFunction<T> action)
    {
        for (uint i = 0; i < _components.Size; i++)
            action(ref _components.GetRef(i), _ecs.FlagEvents);
    }

    public void ForEach(Action<ulong> perIdFunction)
    {
        for (uint i = 0; i < _components.Size; i++)
            perIdFunction(_componentsToIds[i]);
    }

    public uint IdToIndex(ulong entityId) => _idsToComponents[entityId];
    public ulong IndexToId(uint index) => _componentsToIds[index];

    public byte[] GetComponentData(ulong entityId)
    {
        T component = _components[(int)_idsToComponents[entityId]];
        int size = Marshal.SizeOf<T>();
        byte[] data = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(component, ptr, false);
            Marshal.Copy(ptr, data, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return data;
    }

    public void ApplyComponentData(ulong entityId, byte[] data)
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        T component;
        try
        {
            Marshal.Copy(data, 0, ptr, size);
            component = Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        if (HasComponent(entityId))
            _components.GetRef(_idsToComponents[entityId]) = component;
        else
            AddComponent(entityId, component);
    }

    void IComponentStore.RemoveComponent(ulong entityId) => RemoveComponent(entityId);

    public void Clear()
    {
        _components.Clear();
        _idsToComponents.Clear();
        _componentsToIds.Clear();
    }
}
