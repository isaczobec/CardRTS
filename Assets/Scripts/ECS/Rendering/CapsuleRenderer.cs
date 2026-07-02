using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders troop entities that carry RenderableType.Capsule.
/// Each entity gets a Unity primitive capsule positioned at its PositionComponent.
/// </summary>
public class CapsuleRenderer : IComponentRenderer
{
    private readonly ECS _ecs;
    private readonly Dictionary<ulong, GameObject> _objects = new();
    private readonly Material _material;

    // Capsule primitive is 2 units tall; offset by 1 so it stands on the ground plane.
    private const float GroundOffset = 1f;

    public CapsuleRenderer(ECS ecs, Material material = null)
    {
        _ecs = ecs;
        _material = material;
    }

    public void OnEntityAdded(ulong entityId)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"Troop_{entityId}";
        if (_material != null)
            go.GetComponent<MeshRenderer>().sharedMaterial = _material;
        _objects[entityId] = go;
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (!_objects.TryGetValue(entityId, out var go)) return;
        Object.Destroy(go);
        _objects.Remove(entityId);
    }

    public void Update(List<ulong> entityIds)
    {
        var posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_objects.TryGetValue(id, out var go)) continue;
            if (!posStore.HasComponent(id)) continue;

            PositionComponent pos = posStore.GetComponent(id);

            float h = 0f;
            if (WorldManager.instance?.Handler != null)
            {
                ushort maxTile = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
                ushort tx = (ushort)Mathf.Clamp(pos.X, 0, maxTile);
                ushort ty = (ushort)Mathf.Clamp(pos.Y, 0, maxTile);
                h = WorldManager.instance.Handler.GetHeight(tx, ty);
            }

            go.transform.position = new Vector3(pos.X, h + GroundOffset, pos.Y);
        }
    }
}
