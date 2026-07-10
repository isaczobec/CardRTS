using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders building entities. Buildings are stationary, so unlike BasicTroopRenderer this
/// only spawns a prefab once activation fires and destroys it when the entity is removed
/// — no per-frame position interpolation, rotation, or animator triggers.
/// Register an instance with RenderableManager for RenderableType.BasicBuilding.
/// </summary>
public class BasicBuildingRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _prefab;

    private const float GroundOffset = 0f;

    private ECS _ecs;
    private readonly Dictionary<ulong, GameObject> _objects = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
    }

    public void OnEntityAdded(ulong entityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same as troops.
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out GameObject go))
            Destroy(go);
        _objects.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _prefab == null || _ecs == null) return;

        var posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return;

        GameObject go = Instantiate(_prefab);
        go.name = $"Building_{entityId}";
        go.transform.position = ToWorldPosition(posStore.GetComponent(entityId));
        _objects[entityId] = go;
    }

    // Buildings don't move — nothing to do per frame.
    public void UpdateRenderable(List<ulong> entityIds) { }

    private Vector3 ToWorldPosition(PositionComponent pos)
    {
        float h = 0f;
        if (WorldManager.instance?.Handler != null)
        {
            ushort maxTile = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
            ushort tx = (ushort)Mathf.Clamp(pos.X, 0, maxTile);
            ushort ty = (ushort)Mathf.Clamp(pos.Y, 0, maxTile);
            h = WorldManager.instance.Handler.GetHeight(tx, ty);
        }

        return new Vector3(pos.X, h + GroundOffset, pos.Y);
    }
}
