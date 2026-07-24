using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic IModifierRenderer implementation: spawns _prefab above the target troop (offset
/// by _heightOffset) once a modifier of this renderer's type activates on it, and
/// repositions it every frame to follow the target's tick-interpolated position (see
/// EntityPositionQuery) — the same smoothed frame-to-frame follow every other per-entity
/// renderer (health bars, modifier icons, the selection ring, ...) already has, rather than
/// snapping to the target's raw PositionComponent once per simulation tick. One instance
/// handles one RenderableModifierType — register it against that type via
/// RenderableModifierManager's Inspector list, the same way e.g. AoeSpellRenderer is one
/// prefab/settings pair per RenderableType.
/// </summary>
public class PrefabModifierRenderer : MonoBehaviour, IModifierRenderer
{
    [SerializeField] private GameObject _prefab;
    [SerializeField] private float _heightOffset = 3f;

    private ECS _ecs;
    private readonly TickPositionInterpolator _interpolator = new();
    private readonly Dictionary<ulong, GameObject> _objects = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
    }

    public void OnEntityAdded(ulong targetEntityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same deploy-delay
        // convention every other renderer in this project follows.
    }

    public void OnEntityRemoved(ulong targetEntityId)
    {
        // go can already be a destroyed (Unity "fake null") reference here — e.g. a
        // one-shot VFX prefab (ExpandingCirclePrefab) that destroys itself once its own
        // animation finishes, well before the modifier's own (often longer) duration ends.
        // Destroy(null) is a harmless no-op, but checking explicitly avoids relying on that.
        if (_objects.TryGetValue(targetEntityId, out GameObject go) && go != null)
            Destroy(go);
        _objects.Remove(targetEntityId);
        _interpolator.Remove(targetEntityId);
    }

    public void OnEntityActivated(ulong targetEntityId)
    {
        // Can fire more than once for the same target (see IModifierRenderer) — guarded the
        // same way every other renderer's OnEntityActivated already guards against re-adding.
        // Checked against null, not just key presence — a prefab that already self-destroyed
        // (see OnEntityRemoved's own comment) would otherwise permanently block a fresh one
        // from ever being spawned on a later activation.
        if (_prefab == null) return;
        if (_objects.TryGetValue(targetEntityId, out GameObject existing) && existing != null) return;
        if (!EntityPositionQuery.TryGetInterpolatedPosition(_ecs, _interpolator, targetEntityId, _heightOffset, out Vector3 worldPos)) return;

        GameObject go = Instantiate(_prefab, worldPos, Quaternion.identity);
        go.name = $"Modifier_{targetEntityId}";
        _objects[targetEntityId] = go;
    }

    public void UpdateRenderable(List<ulong> targetEntityIds)
    {
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
                continue;
            }
            if (!EntityPositionQuery.TryGetInterpolatedPosition(_ecs, _interpolator, id, _heightOffset, out Vector3 worldPos)) continue; // target no longer exists

            go.transform.position = worldPos;
        }
    }
}
