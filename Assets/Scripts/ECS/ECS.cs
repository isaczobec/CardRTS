using System;
using System.Collections.Generic;
using System.ComponentModel;

public class ECS
{
    public static readonly uint ENTITIES_CAPACITY = 1048576;

    public FlagEventManager FlagEvents { get; } = new FlagEventManager();

    private Dictionary<Type, IComponentStore> _componentStores = new();
    private CopyBackArray<EntityData> _entities;
    private List<ISystem> _systems = new();
    private ComponentDeltaManager _deltaManager;
    private ComponentDeltaManager Delta => _deltaManager; 

    public ECS()
    {
        _entities = new CopyBackArray<EntityData>(ENTITIES_CAPACITY);
        _deltaManager = new ComponentDeltaManager(this, TickManager.instance.ComponentTypeRegistry);
    }

    public void AddComponentStore<T>(ComponentStore<T> componentStore) where T : struct, IComponent
    {
        if (_componentStores.ContainsKey(typeof(T)))
        {
            DebugLogger.LogError($"The ECS already contains a component store for type {typeof(T).Name}");
            return;
        }

        _componentStores[typeof(T)] = componentStore;
        componentStore.Initialize(this);
    }

    public ComponentStore<T> GetComponentStore<T>() where T : struct, IComponent
    {
        if (!_componentStores.ContainsKey(typeof(T)))
        {
            DebugLogger.LogError($"The ECS does not contain a component store for type {typeof(T).Name}");
            return null;
        }
        return (ComponentStore<T>)_componentStores[typeof(T)];
    }

    public IComponentStore GetIComponentStore(Type type)
    {
        if (!_componentStores.ContainsKey(type))
        {
            DebugLogger.LogError($"The ECS does not contain a component store for type {type.Name}");
            return null;
        }
        return _componentStores[type];
    }

    public void AddComponent<T>(ulong entityId, T component = default) where T : struct, IComponent
    {
        ComponentStore<T> store = GetComponentStore<T>();
        if (store == null) return;
        if (store.HasComponent(entityId))
        {
            DebugLogger.LogError($"The entity {entityId} already has a component of type {typeof(T).Name}"); 
            return;
        }
        store.AddComponent(entityId, component);
        FlagEvents.Add<ComponentAddedEvent<T>>();
    }

    public EntityHandle CreateEntity()
    {
        ulong id = (ulong)UnityEngine.Random.Range(0, 9999999999);
        _entities.Add(new EntityData(id));
        FlagEvents.Add<EntityCreatedEvent>();
        return new EntityHandle(id);
    }

    public void RegisterSystem(ISystem system)
    {
        foreach (Type type in system.ComponentTypes)
        {
            if (!_componentStores.ContainsKey(type))
            {
                DebugLogger.LogError($"The ECS does not contain a component store for {type.Name}!");
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
