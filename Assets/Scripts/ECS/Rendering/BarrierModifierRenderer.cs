using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IModifierRenderer for the Barrier buff: spawns _prefab above the target troop (offset by
/// _heightOffset) once the modifier activates and repositions it every frame to follow the
/// target's tick-interpolated position (see EntityPositionQuery) — the same smoothed
/// frame-to-frame follow PrefabModifierRenderer uses, rather than snapping to the target's
/// raw PositionComponent once per simulation tick — and additionally drives a per-instance
/// material's "_Opacity" property (0-1) from the barrier's own remaining-health ratio —
/// BarrierComponent.HealthRemaining / MaxHealth, read off whichever active Barrier modifier
/// entity currently targets this troop (see BarrierSystem) — so the visual fades out as the
/// barrier depletes instead of staying at constant intensity right up until it breaks
/// outright.
/// </summary>
public class BarrierModifierRenderer : MonoBehaviour, IModifierRenderer
{
    [SerializeField] private GameObject _prefab;
    // Template material using a shader with an "_Opacity" property — a fresh instance is
    // made per spawned prefab (see ApplyMaterial) so each target's barrier fades
    // independently instead of every barrier fighting over one shared material's property
    // values, mirroring HealthBarPrefab's own per-instance material convention.
    [SerializeField] private Material _material;
    [SerializeField] private float _heightOffset = 3f;

    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    private ECS _ecs;
    private readonly TickPositionInterpolator _interpolator = new();
    private ComponentStore<ModifierComponent> _modifierStore;
    private ComponentStore<BarrierComponent> _barrierStore;

    private readonly Dictionary<ulong, GameObject> _objects = new();
    private readonly Dictionary<ulong, Material> _materials = new();

    // Rebuilt once per UpdateRenderable call rather than re-walking _barrierStore once per
    // target — cheap regardless of how many troops this renderer is currently tracking.
    private readonly Dictionary<ulong, (float remaining, float max)> _healthByTarget = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _modifierStore = ecs.GetComponentStore<ModifierComponent>();
        _barrierStore = ecs.GetComponentStore<BarrierComponent>();
    }

    public void OnEntityAdded(ulong targetEntityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same deploy-delay
        // convention every other renderer in this project follows.
    }

    public void OnEntityRemoved(ulong targetEntityId)
    {
        // go can already be a destroyed (Unity "fake null") reference if the prefab manages
        // its own lifetime (e.g. a one-shot VFX) — Destroy(null) is a harmless no-op, but
        // checking explicitly avoids relying on that (mirrors PrefabModifierRenderer).
        if (_objects.TryGetValue(targetEntityId, out GameObject go) && go != null)
            Destroy(go);
        _objects.Remove(targetEntityId);
        _interpolator.Remove(targetEntityId);

        DestroyMaterial(targetEntityId);
    }

    public void OnEntityActivated(ulong targetEntityId)
    {
        // Can fire more than once for the same target (see IModifierRenderer) — guarded
        // against null, not just key presence, so a prefab that already destroyed itself
        // doesn't permanently block a fresh one from spawning on a later activation.
        if (_prefab == null) return;
        if (_objects.TryGetValue(targetEntityId, out GameObject existing) && existing != null) return;
        if (!EntityPositionQuery.TryGetInterpolatedPosition(_ecs, _interpolator, targetEntityId, _heightOffset, out Vector3 worldPos)) return;

        GameObject go = Instantiate(_prefab, worldPos, Quaternion.identity);
        go.name = $"Modifier_{targetEntityId}";
        _objects[targetEntityId] = go;

        Material material = ApplyMaterial(go);
        _materials[targetEntityId] = material;
        ApplyOpacity(material, FindHealth(targetEntityId));
    }

    public void UpdateRenderable(List<ulong> targetEntityIds)
    {
        RebuildHealthByTarget();

        foreach (ulong id in targetEntityIds)
        {
            if (!_objects.TryGetValue(id, out GameObject go)) continue;
            if (go == null)
            {
                // Prefab destroyed itself independently of OnEntityRemoved (see that
                // method's own comment) — drop the stale reference instead of touching it
                // again; OnEntityActivated will spawn a fresh one if this modifier is still
                // active and fires again.
                _objects.Remove(id);
                _interpolator.Remove(id);
                DestroyMaterial(id);
                continue;
            }
            if (!EntityPositionQuery.TryGetInterpolatedPosition(_ecs, _interpolator, id, _heightOffset, out Vector3 worldPos)) continue; // target no longer exists

            go.transform.position = worldPos;

            if (_materials.TryGetValue(id, out Material material) && material != null)
            {
                _healthByTarget.TryGetValue(id, out (float remaining, float max) health);
                ApplyOpacity(material, health);
            }
        }
    }

    // Instantiates a fresh copy of _material and assigns it to every renderer under go —
    // covers both a prefab whose mesh sits directly on its root and one whose visual is
    // nested under it, without assuming anything about the prefab's own hierarchy.
    private Material ApplyMaterial(GameObject go)
    {
        if (_material == null) return null;

        Material material = Instantiate(_material);
        foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            renderer.material = material;
        return material;
    }

    private void ApplyOpacity(Material material, (float remaining, float max) health)
    {
        if (material == null) return;
        float opacity = health.max > 0f ? Mathf.Clamp01(health.remaining / health.max) : 0f;
        material.SetFloat(OpacityId, opacity);
    }

    // The instantiated material isn't a scene asset Unity tracks/destroys on its own —
    // without this it'd leak one Material object per barrier for the life of the process
    // (mirrors HealthBarPrefab.OnDestroy).
    private void DestroyMaterial(ulong targetEntityId)
    {
        if (_materials.TryGetValue(targetEntityId, out Material material) && material != null)
            Destroy(material);
        _materials.Remove(targetEntityId);
    }

    // One pass over every currently active Barrier modifier per frame, keyed by target.
    private void RebuildHealthByTarget()
    {
        _healthByTarget.Clear();
        if (_modifierStore == null || _barrierStore == null) return;

        _barrierStore.ForEach((ulong modifierId) =>
        {
            if (!_modifierStore.HasComponent(modifierId)) return;
            if (!ModifierQuery.IsActive(_ecs, modifierId)) return;

            ModifierComponent modifier = _modifierStore.GetComponent(modifierId);
            BarrierComponent barrier = _barrierStore.GetComponent(modifierId);
            _healthByTarget[modifier.TargetEntityId] = (barrier.HealthRemaining, barrier.MaxHealth);
        });
    }

    // Only used by OnEntityActivated, for the very first frame a fresh prefab is spawned —
    // UpdateRenderable's own per-frame pass (via _healthByTarget) takes over from there.
    private (float remaining, float max) FindHealth(ulong targetEntityId)
    {
        if (_modifierStore == null || _barrierStore == null) return (0f, 0f);

        (float remaining, float max) health = (0f, 0f);
        _barrierStore.ForEach((ulong modifierId) =>
        {
            if (!_modifierStore.HasComponent(modifierId)) return;
            if (_modifierStore.GetComponent(modifierId).TargetEntityId != targetEntityId) return;
            if (!ModifierQuery.IsActive(_ecs, modifierId)) return;

            BarrierComponent barrier = _barrierStore.GetComponent(modifierId);
            health = (barrier.HealthRemaining, barrier.MaxHealth);
        });
        return health;
    }
}
