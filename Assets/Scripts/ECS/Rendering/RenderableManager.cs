using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives IComponentRenderer instances by diffing the RenderableComponent store each frame.
/// Call Initialize(ecs) before use, then Register() for each RenderableType you want handled.
/// More than one renderer can be registered for the same RenderableType — e.g. a status
/// effect needing both an overlay-material renderer and a spawned-prefab renderer, without
/// writing a single combined script for it — all of them are notified of the same
/// add/remove/update events for that type.
/// </summary>
public class RenderableManager : MonoBehaviour
{
    [System.Serializable]
    public class ComponentRendererRecordEntry
    {
        [SerializeField] public RenderableType type;
        [SerializeField] public MonoBehaviour renderer;
    }

    [SerializeField] private List<ComponentRendererRecordEntry> _rendererEntries;

    private ECS _ecs;

    private readonly Dictionary<RenderableType, List<IComponentRenderer>> _renderers  = new();
    private readonly Dictionary<RenderableType, HashSet<ulong>>           _tracked    = new();
    private readonly Dictionary<RenderableType, HashSet<ulong>>           _current    = new();
    private readonly Dictionary<RenderableType, List<ulong>>              _entityLists = new();

    // Scratch lists reused every frame to avoid per-frame allocation.
    private readonly List<ulong> _toAdd    = new();
    private readonly List<ulong> _toRemove = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnTroopActivated);

        foreach (var entry in _rendererEntries) {
            Register(entry.type, (IComponentRenderer) entry.renderer);
        }
    }

    private void OnTroopActivated(EntityActivatedEvent e)
    {
        if (_ecs == null) return;

        var store = _ecs.GetComponentStore<RenderableComponent>();
        if (store == null || !store.HasComponent(e.EntityId)) return;

        RenderableType type = store.GetComponent(e.EntityId).Type;
        if (_renderers.TryGetValue(type, out List<IComponentRenderer> renderers))
            foreach (IComponentRenderer renderer in renderers)
                renderer.OnEntityActivated(e.EntityId);
    }

    /// <summary>
    /// Registers a renderer to handle all entities whose RenderableComponent.Type matches
    /// type, alongside any other renderer(s) already registered for that same type (each
    /// call adds one more rather than replacing).
    /// </summary>
    public void Register(RenderableType type, IComponentRenderer renderer)
    {
        if (!_renderers.TryGetValue(type, out List<IComponentRenderer> renderers))
        {
            renderers = new List<IComponentRenderer>();
            _renderers[type]    = renderers;
            _tracked[type]      = new HashSet<ulong>();
            _current[type]      = new HashSet<ulong>();
            _entityLists[type]  = new List<ulong>();
        }

        renderers.Add(renderer);
        renderer.Initialize(_ecs);
    }

    // Finds whichever registered renderer(s) currently own entityId and returns the first
    // non-null/non-empty renderer list among them (see IComponentRenderer.GetRenderers) —
    // null if no renderer currently tracks this entity, or none of them expose any.
    public IReadOnlyList<Renderer> GetRenderers(ulong entityId)
    {
        foreach (var (type, current) in _current)
        {
            if (!current.Contains(entityId) || !_renderers.TryGetValue(type, out List<IComponentRenderer> renderers)) continue;

            foreach (IComponentRenderer renderer in renderers)
            {
                IReadOnlyList<Renderer> result = renderer.GetRenderers(entityId);
                if (result != null && result.Count > 0) return result;
            }
        }
        return null;
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

        // Diff each type against the previous frame, fire callbacks, then update — every
        // renderer registered for a type sees the exact same add/remove/update events.
        foreach (var (type, renderers) in _renderers)
        {
            HashSet<ulong> tracked = _tracked[type];
            HashSet<ulong> current = _current[type];

            _toAdd.Clear();
            _toRemove.Clear();

            foreach (ulong id in current)
                if (!tracked.Contains(id)) _toAdd.Add(id);
            foreach (ulong id in tracked)
                if (!current.Contains(id)) _toRemove.Add(id);

            foreach (IComponentRenderer renderer in renderers)
            {
                foreach (ulong id in _toAdd)    renderer.OnEntityAdded(id);
                foreach (ulong id in _toRemove) renderer.OnEntityRemoved(id);
            }

            tracked.Clear();
            tracked.UnionWith(current);

            foreach (IComponentRenderer renderer in renderers)
                renderer.UpdateRenderable(_entityLists[type]);
        }
    }
}
