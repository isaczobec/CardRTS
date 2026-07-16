using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic IModifierRenderer implementation: spawns _prefab above the target troop (offset
/// by _heightOffset) once a modifier of this renderer's type activates on it, and
/// repositions it every frame to follow the target's live PositionComponent. One instance
/// handles one RenderableModifierType — register it against that type via
/// RenderableModifierManager's Inspector list, the same way e.g. AoeSpellRenderer is one
/// prefab/settings pair per RenderableType.
/// </summary>
public class PrefabModifierRenderer : MonoBehaviour, IModifierRenderer
{
    [SerializeField] private GameObject _prefab;
    [SerializeField] private float _heightOffset = 3f;

    private ComponentStore<PositionComponent> _positionStore;
    private readonly Dictionary<ulong, GameObject> _objects = new();

    public void Initialize(ECS ecs)
    {
        _positionStore = ecs.GetComponentStore<PositionComponent>();
    }

    public void OnEntityAdded(ulong targetEntityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same deploy-delay
        // convention every other renderer in this project follows.
    }

    public void OnEntityRemoved(ulong targetEntityId)
    {
        if (_objects.TryGetValue(targetEntityId, out GameObject go))
            Destroy(go);
        _objects.Remove(targetEntityId);
    }

    public void OnEntityActivated(ulong targetEntityId)
    {
        // Can fire more than once for the same target (see IModifierRenderer) — guarded the
        // same way every other renderer's OnEntityActivated already guards against re-adding.
        if (_objects.ContainsKey(targetEntityId) || _prefab == null) return;
        if (_positionStore == null || !_positionStore.HasComponent(targetEntityId)) return;

        GameObject go = Instantiate(_prefab, WorldPositionFor(_positionStore.GetComponent(targetEntityId)), Quaternion.identity);
        go.name = $"Modifier_{targetEntityId}";
        _objects[targetEntityId] = go;
    }

    public void UpdateRenderable(List<ulong> targetEntityIds)
    {
        if (_positionStore == null) return;

        foreach (ulong id in targetEntityIds)
        {
            if (!_objects.TryGetValue(id, out GameObject go)) continue;
            if (!_positionStore.HasComponent(id)) continue; // target no longer exists

            go.transform.position = WorldPositionFor(_positionStore.GetComponent(id));
        }
    }

    private Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + _heightOffset, pos.Y);
    }
}
