using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public interface IComponent
{

}

public interface ISystem
{
    public Type[] ComponentTypes { get; }
    public void Execute(ECS ecs);
}

public delegate void SingleComponentSystemFunction<T>(ref T component);
public delegate void MultipleComponentSystemFunction(uint[] indicies, ECS ecs);
public class SingleComponentSystem<T> : ISystem where T : struct, IComponent
{
    private SingleComponentSystemFunction<T> _function;
    public Type[] ComponentTypes => new[] { typeof(T) };

    public void Execute(ECS ecs)
    {
        var typed = ecs.GetComponentStore<T>();
        typed.ForEach(_function);
    }
}

public class MultipleComponentSystem : ISystem
{
    public Type[] ComponentTypes => _componentTypes;
    private MultipleComponentSystemFunction _function;
    public uint TypesCount => (uint)_componentTypes.Length;
    Type[] _componentTypes;

    public MultipleComponentSystem(Type[] componentTypes, MultipleComponentSystemFunction func)
    {
        _function = func;
        _componentTypes = componentTypes; 
    }

    public void Execute(ECS ecs)
    {
        IComponentStore shortest = null;
        IComponentStore[] stores = new IComponentStore[TypesCount];
        uint i = 0;
        foreach (Type componentType in _componentTypes)
        {
            IComponentStore s = ecs.GetIComponentStore(componentType);
            stores[i++] = s;
            shortest = s.Size < shortest.Size ? s : shortest;
        }

        shortest.ForEach((ulong id) =>
        {
            bool allExists = true;
            uint[] indicies = new uint[TypesCount];
            uint i = 0;
            foreach (IComponentStore s in stores)
            {
                if (!s.HasComponent(id)) { 
                    allExists = false; 
                    break; 
                } else
                {
                    indicies[i++] = s.IdToIndex(id);
                }
            }
            if (!allExists) return;
            _function(indicies, ecs);
        }
        );         
    }

}

public class EntityHandle
{
    private ulong id;
    public EntityHandle(ulong id) { this.id = id; }
}

public struct EntityData
{
    public ulong Id;
    public EntityData(ulong id) { this.Id = id; }
}

public struct TransformComponent : IComponent
{

}

public interface IComponentStore
{
    public uint Size { get; }
    public Type Type { get; }

    public bool HasComponent(ulong entityId); 
    public void ForEach(Action<ulong> perIdFunction);

    public uint IdToIndex(ulong entityId);
    public ulong IndexToId(uint index);
}

public class ComponentStore<T> : IComponentStore where T : struct, IComponent
{
    private CopyBackArray<T> _components;
    private Dictionary<ulong, uint> _idsToComponents;
    private Dictionary<uint, ulong> _componentsToIds;
    public uint Size => _components.Size;
    public Type Type => typeof(T);

    public ComponentStore()
    {
        _components = new CopyBackArray<T>(ECS.ENTITIES_CAPACITY);
        _idsToComponents = new Dictionary<ulong, uint>();
        _componentsToIds = new Dictionary<uint, ulong>();
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

    public void ForEach(SingleComponentSystemFunction<T> action)
    {
        for (uint i = 0; i < _components.Size; i++)
            action(ref _components.GetRef(i));
    }

    public void ForEach(Action<ulong> perIdFunction)
    {
        for (uint i = 0; i < _components.Size; i++)
        {
            ulong id = _componentsToIds[i];
            perIdFunction(id);
        } 
    }

    public bool HasComponent(ulong entityId)
    {
        return _idsToComponents.ContainsKey(entityId);
    }

    public ref T GetComponent(ulong entityId)
    {   
        uint index = _idsToComponents[entityId];
        return ref _components.GetRef(index);
    }

    public uint IdToIndex(ulong entityId)
    {
        return _idsToComponents[entityId];
    }

    public ulong IndexToId(uint index)
    {
        return _componentsToIds[index];
    }
}

public abstract class FlagEvent
{
    public abstract byte[] Serialize();
    public abstract void Deserialize(byte[] data);
    public virtual bool ShouldNetwork() {return true;}
}

public class FlagEventManager
{
    
}

public class ECS
{
    public static readonly uint ENTITIES_CAPACITY = 1048576;
    private Dictionary<Type, IComponentStore> _componentStores;
    private CopyBackArray<EntityData> _entities;
    private List<ISystem> _systems;

    public ECS()
    {
        _entities = new CopyBackArray<EntityData>(ENTITIES_CAPACITY);
        _componentStores = new Dictionary<Type, IComponentStore>();
        _systems = new List<ISystem>();
    }

    public void AddComponentStore<T>(ComponentStore<T> componentStore) where T : struct, IComponent
    {
        if (_componentStores.ContainsKey(typeof(T)))
        {
            DebugLogger.LogError($"The ECS already contains a component store for type {typeof(T).Name}");
            return;
        }

        _componentStores[typeof(T)] = componentStore;
    }

    public ComponentStore<T> GetComponentStore<T>() where T : struct, IComponent
    {
        if (!_componentStores.ContainsKey(typeof(T)))
        {
            DebugLogger.LogError($"The ECS does not contain a component store for type {typeof(T).Name}");
            return null;
        }
        return (ComponentStore<T>) _componentStores[typeof(T)];
    }

    public IComponentStore GetIComponentStore(Type T)
    {
        if (!_componentStores.ContainsKey(T))
        {
            DebugLogger.LogError($"The ECS does not contain a component store for type {T}");
            return null;
        }
        return _componentStores[T];
    }

    public EntityHandle CreateEntity()
    {
        ulong id = (ulong)UnityEngine.Random.Range(0, 9999999999);
        _entities.Add(new EntityData(id));
        return new EntityHandle(id);
    }

    public void RegisterSystem(ISystem system)
    {
        Type[] componentTypes = system.ComponentTypes;
        foreach (Type componentType in componentTypes)
        {
            if (!_componentStores.ContainsKey(componentType))
            {
                DebugLogger.LogError($"The ECS did not contain a component store for {componentType}!");
                return;
            }
        }

        _systems.Add(system);
    }

    public void ExecuteSystems()
    {
        foreach (var system in _systems)
            system.Execute(this);
    }
}
