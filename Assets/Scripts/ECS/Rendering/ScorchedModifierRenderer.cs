using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IModifierRenderer for the Scorched debuff: spawns _prefab above the target troop (offset
/// by _heightOffset) once the modifier activates, repositions it every frame to follow the
/// target's live PositionComponent (mirrors PrefabModifierRenderer), and additionally scales
/// it up with the debuff's current stack count — StackingBurnDebuffComponent.Stacks, read off
/// whichever active Scorched modifier entity currently targets this troop (see
/// ProjectileOnHitSystem.ApplyScorch) — so a heavily-stacked burn visibly reads as more
/// intense than a fresh one.
/// </summary>
public class ScorchedModifierRenderer : MonoBehaviour, IModifierRenderer
{
    [SerializeField] private GameObject _prefab;
    [SerializeField] private float _heightOffset = 3f;

    [Header("Stack Scaling")]
    // Uniform scale at 0 stacks (a fresh, unstacked application).
    [SerializeField] private float _baseScale = 1f;
    // Added to the scale multiplier per stack — e.g. 0.15 at 5 max stacks reads up to
    // baseScale x 1.75 at full stacks.
    [SerializeField] private float _scalePerStack = 0.15f;

    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<ModifierComponent> _modifierStore;
    private ComponentStore<StackingBurnDebuffComponent> _stackStore;
    private readonly Dictionary<ulong, GameObject> _objects = new();

    // Rebuilt once per UpdateRenderable call rather than re-walking _stackStore once per
    // target — cheap regardless of how many troops this renderer is currently tracking.
    private readonly Dictionary<ulong, int> _stacksByTarget = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _modifierStore = ecs.GetComponentStore<ModifierComponent>();
        _stackStore = ecs.GetComponentStore<StackingBurnDebuffComponent>();
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
        // checking explicitly avoids relying on that.
        if (_objects.TryGetValue(targetEntityId, out GameObject go) && go != null)
            Destroy(go);
        _objects.Remove(targetEntityId);
    }

    public void OnEntityActivated(ulong targetEntityId)
    {
        // Can fire more than once for the same target (see IModifierRenderer) — guarded
        // against null, not just key presence, so a prefab that already destroyed itself
        // doesn't permanently block a fresh one from spawning on a later activation.
        if (_prefab == null) return;
        if (_objects.TryGetValue(targetEntityId, out GameObject existing) && existing != null) return;
        if (_positionStore == null || !_positionStore.HasComponent(targetEntityId)) return;

        GameObject go = Instantiate(_prefab, WorldPositionFor(_positionStore.GetComponent(targetEntityId)), Quaternion.identity);
        go.name = $"Modifier_{targetEntityId}";
        go.transform.localScale = Vector3.one * ScaleForStacks(FindStacks(targetEntityId));
        _objects[targetEntityId] = go;
    }

    public void UpdateRenderable(List<ulong> targetEntityIds)
    {
        if (_positionStore == null) return;

        RebuildStacksByTarget();

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
                continue;
            }
            if (!_positionStore.HasComponent(id)) continue; // target no longer exists

            go.transform.position = WorldPositionFor(_positionStore.GetComponent(id));

            _stacksByTarget.TryGetValue(id, out int stacks);
            go.transform.localScale = Vector3.one * ScaleForStacks(stacks);
        }
    }

    // One pass over every currently active Scorched modifier per frame, keyed by target.
    private void RebuildStacksByTarget()
    {
        _stacksByTarget.Clear();
        if (_modifierStore == null || _stackStore == null) return;

        _stackStore.ForEach((ulong modifierId) =>
        {
            if (!_modifierStore.HasComponent(modifierId)) return;
            if (!ModifierQuery.IsActive(_ecs, modifierId)) return;

            ModifierComponent modifier = _modifierStore.GetComponent(modifierId);
            _stacksByTarget[modifier.TargetEntityId] = _stackStore.GetComponent(modifierId).Stacks;
        });
    }

    // Only used by OnEntityActivated, for the very first frame a fresh prefab is spawned —
    // UpdateRenderable's own per-frame pass (via _stacksByTarget) takes over from there.
    private int FindStacks(ulong targetEntityId)
    {
        if (_modifierStore == null || _stackStore == null) return 0;

        int stacks = 0;
        _stackStore.ForEach((ulong modifierId) =>
        {
            if (!_modifierStore.HasComponent(modifierId)) return;
            if (_modifierStore.GetComponent(modifierId).TargetEntityId != targetEntityId) return;
            if (!ModifierQuery.IsActive(_ecs, modifierId)) return;
            stacks = _stackStore.GetComponent(modifierId).Stacks;
        });
        return stacks;
    }

    private float ScaleForStacks(int stacks) => _baseScale * (1f + stacks * _scalePerStack);

    private Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + _heightOffset, pos.Y);
    }
}
