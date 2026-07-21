using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives IModifierRenderer instances by diffing the RenderableModifierComponent store each
/// frame — mirrors RenderableManager, but tracks by ModifierComponent.TargetEntityId (the
/// troop/building a modifier is attached to) rather than the modifier entity's own id, using
/// reference counting to collapse a client-predicted modifier and the server's later
/// replacement (a separate entity id — see ModifierComponent) into a single add/remove pair
/// per target: whichever modifier entity for a given (type, target) is seen first "wins" the
/// existing visual (a 'first-best' match — no new visual is created for the replacement),
/// and the visual is only torn down once every modifier entity currently mapped to that
/// (type, target) is gone. Call Initialize(ecs) before use, then Register() for each
/// RenderableModifierType you want handled. More than one renderer can be registered for the
/// same RenderableModifierType — e.g. a status effect needing both an overlay-material
/// renderer and a spawned-prefab renderer, without writing a single combined script for it —
/// all of them are notified of the same add/remove/update events (and share the same
/// reference-counted target set) for that type.
/// </summary>
public class RenderableModifierManager : MonoBehaviour
{
    [System.Serializable]
    public class ModifierRendererRecordEntry
    {
        [SerializeField] public RenderableModifierType type;
        [SerializeField] public MonoBehaviour renderer;
    }

    [SerializeField] private List<ModifierRendererRecordEntry> _rendererEntries;

    private ECS _ecs;

    private readonly Dictionary<RenderableModifierType, List<IModifierRenderer>> _renderers = new();
    private readonly Dictionary<RenderableModifierType, HashSet<ulong>> _trackedModifierIds = new();
    private readonly Dictionary<RenderableModifierType, HashSet<ulong>> _currentModifierIds = new();
    private readonly Dictionary<RenderableModifierType, Dictionary<ulong, int>> _targetRefCounts = new();
    private readonly Dictionary<RenderableModifierType, List<ulong>> _targetLists = new();

    // Cached at add-time and consulted at remove-time: by the time a removal is diffed, the
    // modifier entity is already gone from the live component store, so its
    // ModifierComponent.TargetEntityId can no longer be read at that point. Keyed globally
    // (not per-type) since modifier entity ids are unique across the whole ECS.
    private readonly Dictionary<ulong, ulong> _modifierEntityToTarget = new();

    // Scratch lists reused every frame to avoid per-frame allocation.
    private readonly List<ulong> _toAdd = new();
    private readonly List<ulong> _toRemove = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnModifierActivated);

        foreach (var entry in _rendererEntries) {
            Register(entry.type, (IModifierRenderer) entry.renderer);
        }
    }

    private void OnModifierActivated(EntityActivatedEvent e)
    {
        if (_ecs == null) return;

        var renderableStore = _ecs.GetComponentStore<RenderableModifierComponent>();
        if (renderableStore == null || !renderableStore.HasComponent(e.EntityId)) return;

        var modifierStore = _ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore == null || !modifierStore.HasComponent(e.EntityId)) return;

        RenderableModifierType type = renderableStore.GetComponent(e.EntityId).Type;
        if (!_renderers.TryGetValue(type, out List<IModifierRenderer> renderers)) return;

        ulong targetEntityId = modifierStore.GetComponent(e.EntityId).TargetEntityId;
        foreach (IModifierRenderer renderer in renderers)
            renderer.OnEntityActivated(targetEntityId);
    }

    /// <summary>
    /// Registers a renderer to handle all modifier entities whose RenderableModifierComponent.Type
    /// matches type, alongside any other renderer(s) already registered for that same type
    /// (each call adds one more rather than replacing).
    /// </summary>
    public void Register(RenderableModifierType type, IModifierRenderer renderer)
    {
        if (!_renderers.TryGetValue(type, out List<IModifierRenderer> renderers))
        {
            renderers = new List<IModifierRenderer>();
            _renderers[type]          = renderers;
            _trackedModifierIds[type] = new HashSet<ulong>();
            _currentModifierIds[type] = new HashSet<ulong>();
            _targetRefCounts[type]    = new Dictionary<ulong, int>();
            _targetLists[type]        = new List<ulong>();
        }

        renderers.Add(renderer);
        renderer.Initialize(_ecs);
    }

    void Update()
    {
        if (_ecs == null) return;

        var renderableStore = _ecs.GetComponentStore<RenderableModifierComponent>();
        var modifierStore = _ecs.GetComponentStore<ModifierComponent>();
        if (renderableStore == null || modifierStore == null) return;

        // Reset per-type working sets for this frame.
        foreach (var type in _renderers.Keys)
            _currentModifierIds[type].Clear();

        // Populate from the live component store.
        renderableStore.ForEach((ulong entityId) =>
        {
            RenderableModifierType type = renderableStore.GetComponent(entityId).Type;
            if (!_renderers.ContainsKey(type)) return;
            if (!modifierStore.HasComponent(entityId)) return; // ModifierComponent not attached yet
            _currentModifierIds[type].Add(entityId);
        });

        // Diff each type's modifier-entity-ids against the previous frame, translate to
        // target-entity-id add/remove via reference counting, then update. All renderers
        // registered for a type share this same diff/ref-count pass and see identical events.
        foreach (var (type, renderers) in _renderers)
        {
            HashSet<ulong> tracked = _trackedModifierIds[type];
            HashSet<ulong> current = _currentModifierIds[type];
            Dictionary<ulong, int> refCounts = _targetRefCounts[type];

            _toAdd.Clear();
            _toRemove.Clear();

            foreach (ulong id in current)
                if (!tracked.Contains(id)) _toAdd.Add(id);
            foreach (ulong id in tracked)
                if (!current.Contains(id)) _toRemove.Add(id);

            foreach (ulong modifierId in _toAdd)
            {
                ulong targetId = modifierStore.GetComponent(modifierId).TargetEntityId;
                _modifierEntityToTarget[modifierId] = targetId;

                refCounts.TryGetValue(targetId, out int count);
                refCounts[targetId] = count + 1;
                if (count == 0)
                    foreach (IModifierRenderer renderer in renderers)
                        renderer.OnEntityAdded(targetId);

                // A modifier with no ActivatableComponent at all (e.g. an instant/permanent
                // one like ProjectileOnHitSystem's Chilled) never gets an
                // EntityActivatedEvent — ActivationSystem only ever fires that for entities
                // that actually HAVE the component, counting down to 0. Without this, a
                // renderer that waits for OnEntityActivated (rather than OnEntityAdded)
                // before showing its visual — see e.g. OverlayMaterialModifierRenderer —
                // would just never show it at all for a modifier like that. Firing it here
                // instead, the moment we first see a modifier that's already active, covers
                // that case (and a modifier whose delay finished before this manager's own
                // Update() first noticed it). Safe even if EntityActivatedEvent ALSO fires
                // for the same target — IModifierRenderer.OnEntityActivated is documented to
                // potentially fire more than once, and implementations are expected to
                // guard against that the same way OnEntityAdded ones do.
                if (ActivationQuery.IsActive(_ecs, modifierId))
                    foreach (IModifierRenderer renderer in renderers)
                        renderer.OnEntityActivated(targetId);
            }

            foreach (ulong modifierId in _toRemove)
            {
                if (!_modifierEntityToTarget.TryGetValue(modifierId, out ulong targetId)) continue;
                _modifierEntityToTarget.Remove(modifierId);

                if (!refCounts.TryGetValue(targetId, out int count)) continue;
                count--;
                if (count <= 0)
                {
                    refCounts.Remove(targetId);
                    foreach (IModifierRenderer renderer in renderers)
                        renderer.OnEntityRemoved(targetId);
                }
                else
                {
                    refCounts[targetId] = count;
                }
            }

            tracked.Clear();
            tracked.UnionWith(current);

            List<ulong> targetList = _targetLists[type];
            targetList.Clear();
            targetList.AddRange(refCounts.Keys);

            foreach (IModifierRenderer renderer in renderers)
                renderer.UpdateRenderable(targetList);
        }
    }
}
