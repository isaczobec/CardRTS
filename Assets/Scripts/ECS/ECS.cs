using System;
using System.Collections.Generic;

public class ECS
{
    public static readonly uint ENTITIES_CAPACITY = 1048576;

    public FlagEventManager FlagEvents { get; } = new FlagEventManager();
    public RequestManager Requests { get; } = new RequestManager();

    // Set by TickManager before each ExecuteSystems() call so systems can query inputs for the right tick.
    public ulong CurrentSimulationTick { get; set; }

    private Dictionary<Type, IComponentStore> _componentStores = new();
    private CopyBackArray<EntityData> _entities;
    private Dictionary<ulong, int> _entityIdsToIndicies;
    private List<ISystem> _systems = new();
    private ComponentDeltaManager _deltaManager;
    public ComponentDeltaManager Delta => _deltaManager;

    public EntityChunkTracker ChunkTracker { get; } = new EntityChunkTracker();

    public ECS()
    {
        _entities = new CopyBackArray<EntityData>(ENTITIES_CAPACITY);
        _entityIdsToIndicies = new();
        _deltaManager = new ComponentDeltaManager(this, TickManager.instance.ComponentTypeRegistry);
        ChunkTracker.Initialize(this);
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
        Delta.MarkComponentDirty(entityId, typeof(T));
        FlagEvents.Add(new ComponentAddedEvent<T> { EntityId = entityId });
    }

    public void RemoveComponent<T>(ulong entityId) where T : struct, IComponent
    {
        ComponentStore<T> store = GetComponentStore<T>();
        if (store == null) return;
        if (!store.HasComponent(entityId))
        {
            DebugLogger.LogError($"The entity {entityId} does not have a component of type {typeof(T).Name}"); 
            return;
        }
        store.RemoveComponent(entityId);
        Delta.MarkComponentDeleted(entityId, typeof(T));
        FlagEvents.Add(new ComponentRemovedEvent<T> { EntityId = entityId });
    }

    public EntityHandle CreateEntity()
    {
        ulong id = NextEntityId();

        var addRes = _entities.Add(new EntityData(id));
        _entityIdsToIndicies[id] = (int) addRes.addedIndex;

        FlagEvents.Add(new EntityCreatedEvent { EntityId = id });
        Delta.MarkEntityCreated(id);
        return new EntityHandle(id);
    }

    // Combines two full-range int draws into a ulong for real ~64-bit spread. A single
    // UnityEngine.Random.Range(0, 9999999999) call resolves to the float overload (the
    // upper bound doesn't fit int), and float's ~24-bit mantissa can't represent values
    // near 1e10 precisely — collisions become plausible once many entities have been
    // created, silently clobbering _entityIdsToIndicies for two still-alive entities.
    private static ulong NextEntityId()
    {
        ulong id;
        do
        {
            uint hi = unchecked((uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            uint lo = unchecked((uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            id = ((ulong)hi << 32) | lo;
        } while (id == 0); // 0 is used elsewhere as an EntityId "none" sentinel

        return id;
    }

    public bool HasEntity(ulong entityId) => _entityIdsToIndicies.ContainsKey(entityId);

    public void CreateEntityWithId(ulong id)
    {
        var addRes = _entities.Add(new EntityData(id));
        _entityIdsToIndicies[id] = (int)addRes.addedIndex;
        FlagEvents.Add(new EntityCreatedEvent { EntityId = id });
    }

    public void DeleteEntity(ulong entityId)
    {
        if (!_entityIdsToIndicies.ContainsKey(entityId))
        {
            DebugLogger.LogError($"entity {entityId} did not exist but it was attempted to be deleted!");
            return;
        }
        int index = _entityIdsToIndicies[entityId];
        var result = _entities.Remove((uint)index);
        _entityIdsToIndicies.Remove(entityId);

        // The removal swap-moved the entity that used to be at the last slot into this one;
        // its recorded index must be updated or it goes stale and points out of range later.
        if (result.movedValid)
        {
            ulong movedEntityId = _entities[(int)result.removedIndex].Id;
            _entityIdsToIndicies[movedEntityId] = (int)result.removedIndex;
        }

        // Otherwise every component store keeps this entity's data forever — component
        // stores are diffed directly (e.g. RenderableManager against RenderableComponent),
        // so a "deleted" entity would still look alive to anything reading them.
        foreach (IComponentStore store in _componentStores.Values)
            if (store.HasComponent(entityId))
                store.RemoveComponent(entityId);

        Delta.MarkEntityDeleted(entityId);
        FlagEvents.Add(new EntityDeletedEvent { EntityId = entityId });
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

    // Finds a registered system instance by its concrete type, so other code can query
    // state it tracks (e.g. TargetingSystem's target records). Returns null if no
    // system of that type is registered on this ECS.
    public T GetSystem<T>() where T : class, ISystem
    {
        foreach (ISystem system in _systems)
            if (system is T typed) return typed;
        return null;
    }

    // Returns inputs of type T for the given tick, or CurrentSimulationTick if omitted.
    public List<T> GetInputsForTick<T>(ulong? tick = null) where T : InputBase
        => InputBuffer.GetInputsForTick<T>(tick ?? CurrentSimulationTick);

    // Resets all entity and component state. Delta buffers are unaffected (harmless on local ECS).
    public void Clear()
    {
        foreach (var store in _componentStores.Values)
            store.Clear();
        _entities.Clear();
        _entityIdsToIndicies.Clear();
    }

    // Replaces this ECS's state with a full copy of source. Used for reconciliation (mirror → local).
    public void CopyStateFrom(ECS source)
    {
        Clear();

        foreach (ulong id in source._entityIdsToIndicies.Keys)
            CreateEntityWithId(id);

        foreach (var pair in source._componentStores)
        {
            IComponentStore targetStore = GetIComponentStore(pair.Key);
            if (targetStore == null) continue;
            pair.Value.ForEach(entityId =>
            {
                byte[] data = pair.Value.GetComponentData(entityId);
                targetStore.ApplyComponentData(entityId, data);
            });
        }

        ChunkTracker.Rebuild();
    }
}
