using System;
using System.Collections.Generic;
using System.IO;

public class ComponentDeltaManager
{
    private HashSet<ComponentMarker> _dirtyComponents = new();
    private HashSet<ComponentMarker> _deletedComponents = new();
    private HashSet<ulong> _createdEntities = new();
    private HashSet<ulong> _deletedEntities = new();
    private readonly ECS _ecs;
    private readonly TypeRegistry<IComponent> _componentTypeRegistry;

    public struct ComponentMarker : IEquatable<ComponentMarker>
    {
        public ulong EntityId;
        public Type ComponentType;

        public readonly bool Equals(ComponentMarker other) =>
            EntityId == other.EntityId && ComponentType == other.ComponentType;

        public override readonly bool Equals(object obj) =>
            obj is ComponentMarker m && Equals(m);

        public override int GetHashCode() =>
            EntityId.GetHashCode() * 397 ^ ComponentType.GetHashCode();
    }

    public ComponentDeltaManager(ECS ecs, TypeRegistry<IComponent> componentTypeRegistry)
    {
        _ecs = ecs;
        _componentTypeRegistry = componentTypeRegistry;
    }

    public void MarkComponentDirty(ulong entityId, Type componentType)
    {
        _dirtyComponents.Add(new ComponentMarker { EntityId = entityId, ComponentType = componentType });
    }

    public void MarkEntityCreated(ulong entityId)
    {
        _createdEntities.Add(entityId);
    }
    public void MarkEntityDeleted(ulong entityId)
    {
        if (_createdEntities.Contains(entityId))
            _createdEntities.Remove(entityId);
        _deletedEntities.Add(entityId);
    }

    public void MarkComponentDeleted(ulong entityId, Type componentType)
    {
        ComponentMarker marker = new ComponentMarker { EntityId = entityId, ComponentType = componentType };
        if (_dirtyComponents.Contains(marker)) 
            _dirtyComponents.Remove(marker);
        _deletedComponents.Add(marker);
    }

    /// <summary>
    /// Gets and clears the delta of the ECS up to the previous call. 
    /// Wire format: `[count: int32][entityId: ulong][typeId: ushort][dataLen: ushort][data: bytes]...`
    /// </summary>
    /// <returns></returns>
    public byte[] GetComponentsDelta()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(_dirtyComponents.Count);

        foreach (var dirty in _dirtyComponents)
        {
            IComponentStore store = _ecs.GetIComponentStore(dirty.ComponentType);
            if (store == null) continue;

            byte[] data = store.GetComponentData(dirty.EntityId);
            ushort typeId = _componentTypeRegistry.GetIDForType(dirty.ComponentType);

            writer.Write(dirty.EntityId);
            writer.Write(typeId);
            writer.Write((ushort)data.Length);
            writer.Write(data);
        }

        _dirtyComponents.Clear();
        return ms.ToArray();
    }

    /// <summary>
    /// Gets and clears the delta of the ECS up to the previous call. 
    /// Wire format: `[count: int32][entityId: ulong]...`
    /// </summary>
    /// <returns></returns>
    public byte[] GetCreatedEntities()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(_createdEntities.Count);

        foreach (ulong id in _createdEntities)
        {
            writer.Write(id);
        }

        _createdEntities.Clear();
        return ms.ToArray();
    }

    public byte[] GetDeletedEntities()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(_deletedEntities.Count);

        foreach (ulong id in _deletedEntities)
            writer.Write(id);

        _deletedEntities.Clear();
        return ms.ToArray();
    }

    public byte[] GetDeletedComponents()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(_deletedComponents.Count);

        foreach (var marker in _deletedComponents)
        {
            ushort typeId = _componentTypeRegistry.GetIDForType(marker.ComponentType);
            writer.Write(marker.EntityId);
            writer.Write(typeId);
        }

        _deletedComponents.Clear();
        return ms.ToArray();
    }

    /// <summary>
    /// Applies an incoming delta to the ECS. Order: created entities → deleted components → deleted entities → component data.
    /// Creates a component if the entity does not already have one of that type.
    /// </summary>
    public void ApplyDelta(ECS ecs, byte[] createdEntities, byte[] deletedEntities, byte[] deletedComponents, byte[] componentsDelta)
    {
        using (var ms = new MemoryStream(createdEntities))
        using (var reader = new BinaryReader(ms))
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                ulong entityId = reader.ReadUInt64();
                if (!ecs.HasEntity(entityId))
                    ecs.CreateEntityWithId(entityId);
            }
        }

        using (var ms = new MemoryStream(deletedComponents))
        using (var reader = new BinaryReader(ms))
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                ulong entityId = reader.ReadUInt64();
                ushort typeId = reader.ReadUInt16();
                Type componentType = _componentTypeRegistry.GetTypeForID(typeId);
                IComponentStore store = ecs.GetIComponentStore(componentType);
                if (store != null && store.HasComponent(entityId))
                    store.RemoveComponent(entityId);
            }
        }

        using (var ms = new MemoryStream(deletedEntities))
        using (var reader = new BinaryReader(ms))
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                ulong entityId = reader.ReadUInt64();
                if (ecs.HasEntity(entityId))
                    ecs.DeleteEntity(entityId);
            }
        }

        using (var ms = new MemoryStream(componentsDelta))
        using (var reader = new BinaryReader(ms))
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                ulong entityId = reader.ReadUInt64();
                ushort typeId = reader.ReadUInt16();
                ushort dataLen = reader.ReadUInt16();
                byte[] data = reader.ReadBytes(dataLen);
                Type componentType = _componentTypeRegistry.GetTypeForID(typeId);
                IComponentStore store = ecs.GetIComponentStore(componentType);
                store?.ApplyComponentData(entityId, data);
            }
        }
    }
}
