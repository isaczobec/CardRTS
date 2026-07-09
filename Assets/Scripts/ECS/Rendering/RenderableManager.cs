using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives IComponentRenderer instances by diffing the RenderableComponent store each frame.
/// Call Initialize(ecs) before use, then Register() for each RenderableType you want handled.
/// </summary>
public class RenderableManager : MonoBehaviour
{
    private ECS _ecs;

    private readonly Dictionary<RenderableType, IComponentRenderer> _renderers  = new();
    private readonly Dictionary<RenderableType, HashSet<ulong>>     _tracked    = new();
    private readonly Dictionary<RenderableType, HashSet<ulong>>     _current    = new();
    private readonly Dictionary<RenderableType, List<ulong>>        _entityLists = new();

    // Scratch lists reused every frame to avoid per-frame allocation.
    private readonly List<ulong> _toAdd    = new();
    private readonly List<ulong> _toRemove = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        TickManager.instance.ServerFlagEvents.Subscribe<TroopActivatedEvent>(OnTroopActivated);
    }

    private void OnTroopActivated(TroopActivatedEvent e)
    {
        if (_ecs == null) return;

        var store = _ecs.GetComponentStore<RenderableComponent>();
        if (store == null || !store.HasComponent(e.EntityId)) return;

        RenderableType type = store.GetComponent(e.EntityId).Type;
        if (_renderers.TryGetValue(type, out IComponentRenderer renderer))
            renderer.OnEntityActivated(e.EntityId);
    }

    /// <summary>
    /// Registers a renderer to handle all entities whose RenderableComponent.Type matches type.
    /// Replaces any previously registered renderer for that type.
    /// </summary>
    public void Register(RenderableType type, IComponentRenderer renderer)
    {
        _renderers[type]    = renderer;
        _tracked[type]      = new HashSet<ulong>();
        _current[type]      = new HashSet<ulong>();
        _entityLists[type]  = new List<ulong>();

        renderer.Initialize(_ecs);
    }

    void Update()
    {
        if (_ecs == null) return;

        var store = _ecs.GetComponentStore<RenderableComponent>();
        if (store == null) return;

        // Reset per-type working sets for this frame.
        foreach (var type in _renderers.Keys)
        {
            _current[type].Clear();
            _entityLists[type].Clear();
        }

        // Populate from the live component store.
        store.ForEach((ulong entityId) =>
        {
            RenderableType type = store.GetComponent(entityId).Type;
            if (!_renderers.ContainsKey(type)) return;
            _current[type].Add(entityId);
            _entityLists[type].Add(entityId);
        });

        // Diff each type against the previous frame, fire callbacks, then update.
        foreach (var (type, renderer) in _renderers)
        {
            HashSet<ulong> tracked = _tracked[type];
            HashSet<ulong> current = _current[type];

            _toAdd.Clear();
            _toRemove.Clear();

            foreach (ulong id in current)
                if (!tracked.Contains(id)) _toAdd.Add(id);
            foreach (ulong id in tracked)
                if (!current.Contains(id)) _toRemove.Add(id);

            foreach (ulong id in _toAdd)    renderer.OnEntityAdded(id);
            foreach (ulong id in _toRemove) renderer.OnEntityRemoved(id);

            tracked.Clear();
            tracked.UnionWith(current);

            renderer.UpdateRenderable(_entityLists[type]);
        }
    }
}
