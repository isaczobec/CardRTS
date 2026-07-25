using System.Collections.Generic;
using UnityEngine;

// IModifierRenderer for any PeriodicAreaEffectComponent-carrying modifier (currently just
// HealerGuardianCard's own heal aura self-buff) — mirrors PrefabModifierRenderer's own
// spawn-on-activate/follow-interpolated-position shape, but additionally rescales the spawned
// prefab every frame so its radius always matches RangeMultiplier x the target's own Range
// stat, i.e. the EXACT radius PeriodicAreaEffectSystem itself scans (so a Range-affecting
// StatModifierComponent moves the visual in lockstep with the real effect area). _prefab is
// assumed authored at unit scale — radius 1 at localScale (1,1,1) — so applying the computed
// radius as a uniform localScale is enough, no separate mesh-size constant to configure.
public class AreaEffectRadiusModifierRenderer : MonoBehaviour, IModifierRenderer
{
    [SerializeField] private GameObject _prefab;
    // Small — this is a ground-level radius indicator, not a floating-above-the-head icon
    // like BarrierModifierRenderer/PrefabModifierRenderer's own default 3f.
    [SerializeField] private float _heightOffset = 0.05f;

    private const int DefaultRange = 5;

    private ECS _ecs;
    private readonly TickPositionInterpolator _interpolator = new();
    private ComponentStore<ModifierComponent> _modifierStore;
    private ComponentStore<PeriodicAreaEffectComponent> _areaEffectStore;

    private readonly Dictionary<ulong, GameObject> _objects = new();

    // Rebuilt once per UpdateRenderable/OnEntityActivated call rather than re-walking
    // _areaEffectStore once per target — mirrors BarrierModifierRenderer's own
    // _healthByTarget convention.
    private readonly Dictionary<ulong, float> _radiusByTarget = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _modifierStore = ecs.GetComponentStore<ModifierComponent>();
        _areaEffectStore = ecs.GetComponentStore<PeriodicAreaEffectComponent>();
    }

    public void OnEntityAdded(ulong targetEntityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same deploy-delay
        // convention every other renderer in this project follows.
    }

    public void OnEntityRemoved(ulong targetEntityId)
    {
        // go can already be a destroyed (Unity "fake null") reference if the prefab manages
        // its own lifetime — Destroy(null) is a harmless no-op, but checking explicitly
        // avoids relying on that (mirrors BarrierModifierRenderer/PrefabModifierRenderer).
        if (_objects.TryGetValue(targetEntityId, out GameObject go) && go != null)
            Destroy(go);
        _objects.Remove(targetEntityId);
        _interpolator.Remove(targetEntityId);
    }

    public void OnEntityActivated(ulong targetEntityId)
    {
        // Can fire more than once for the same target (see IModifierRenderer) — guarded
        // against null, not just key presence, same as every other renderer here.
        if (_prefab == null) return;
        if (_objects.TryGetValue(targetEntityId, out GameObject existing) && existing != null) return;
        if (!EntityPositionQuery.TryGetInterpolatedPosition(_ecs, _interpolator, targetEntityId, _heightOffset, out Vector3 worldPos)) return;

        GameObject go = Instantiate(_prefab, worldPos, Quaternion.identity);
        go.name = $"Modifier_{targetEntityId}";
        _objects[targetEntityId] = go;

        RebuildRadiusByTarget();
        ApplyRadius(go, targetEntityId);
    }

    public void UpdateRenderable(List<ulong> targetEntityIds)
    {
        RebuildRadiusByTarget();

        foreach (ulong id in targetEntityIds)
        {
            if (!_objects.TryGetValue(id, out GameObject go)) continue;
            if (go == null)
            {
                // Prefab destroyed itself independently of OnEntityRemoved — drop the stale
                // reference instead of touching it again; OnEntityActivated will spawn a
                // fresh one if this modifier is still active and fires again.
                _objects.Remove(id);
                _interpolator.Remove(id);
                continue;
            }
            if (!EntityPositionQuery.TryGetInterpolatedPosition(_ecs, _interpolator, id, _heightOffset, out Vector3 worldPos)) continue; // target no longer exists

            go.transform.position = worldPos;
            ApplyRadius(go, id);
        }
    }

    private void ApplyRadius(GameObject go, ulong targetEntityId)
    {
        if (!_radiusByTarget.TryGetValue(targetEntityId, out float radius)) return;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, radius);
    }

    // One pass over every currently active PeriodicAreaEffectComponent modifier per frame,
    // keyed by target — mirrors BarrierModifierRenderer.RebuildHealthByTarget.
    private void RebuildRadiusByTarget()
    {
        _radiusByTarget.Clear();
        if (_modifierStore == null || _areaEffectStore == null) return;

        _areaEffectStore.ForEach((ulong modifierId) =>
        {
            if (!_modifierStore.HasComponent(modifierId)) return;
            if (!ModifierQuery.IsActive(_ecs, modifierId)) return;

            ModifierComponent modifier = _modifierStore.GetComponent(modifierId);
            PeriodicAreaEffectComponent effect = _areaEffectStore.GetComponent(modifierId);
            float radius = StatsQuery.GetRange(_ecs, modifier.TargetEntityId, DefaultRange) * effect.RangeMultiplier;
            _radiusByTarget[modifier.TargetEntityId] = radius;
        });
    }
}
