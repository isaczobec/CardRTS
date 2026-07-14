using System.Collections.Generic;
using UnityEngine;

// Renders active AoeSpellCard entities (DamageAuraComponent + LifetimeComponent): a flat
// ground disc instantiated at 1-unit diameter and scaled up to the entity's Range stat,
// with a per-instance material continuously fed the entity's elapsed-lifetime fraction
// (AoeSpellPrefab.SetDurationElapsed) so the effect can animate itself out over its
// duration. Register an instance with RenderableManager for RenderableType.AoeSpell.
//
// Stationary — an AOE spell entity has no MovableComponent — so unlike BasicTroopRenderer's
// TickPositionInterpolator, position/scale are only ever set once, at activation.
public class AoeSpellRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private AoeSpellPrefab _prefab;

    private const int DefaultRange = 5;

    private ECS _ecs;
    private readonly Dictionary<ulong, AoeSpellPrefab> _objects = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
    }

    public void OnEntityAdded(ulong entityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same as every
        // other renderer, so a still-deploying spell stays invisible until it's active.
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out AoeSpellPrefab go))
            Destroy(go.gameObject);
        _objects.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _prefab == null) return;

        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return;

        PositionComponent pos = posStore.GetComponent(entityId);
        AoeSpellPrefab go = Instantiate(_prefab, WorldPositionFor(pos), Quaternion.identity);
        go.name = $"AoeSpell_{entityId}";

        float range = StatsQuery.GetRange(_ecs, entityId, DefaultRange);
        go.SetScale(range * 2f); // radius -> diameter
        go.SetDurationElapsed(0f);

        _objects[entityId] = go;
    }

    public void UpdateRenderable(List<ulong> entityIds)
    {
        var lifetimeStore = _ecs?.GetComponentStore<LifetimeComponent>();
        if (lifetimeStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_objects.TryGetValue(id, out AoeSpellPrefab go)) continue;
            if (!lifetimeStore.HasComponent(id)) continue;

            LifetimeComponent lifetime = lifetimeStore.GetComponent(id);
            float elapsed = lifetime.InitialTicksRemaining > 0
                ? 1f - (float)lifetime.TicksRemaining / lifetime.InitialTicksRemaining
                : 0f;
            go.SetDurationElapsed(Mathf.Clamp01(elapsed));
        }
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }
}
