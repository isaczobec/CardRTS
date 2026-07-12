using System.Collections.Generic;
using UnityEngine;

// Renders a respawnable resource building with two states. On RespawnableEntityDiedEvent,
// switches to _respawningPrefab. On RespawnableEntityRespawnedEvent, switches back to
// _alivePrefab. Both GameObjects are instantiated on activation; no per-frame querying.
public class RespawnableBuildingRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _alivePrefab;
    [SerializeField] private GameObject _respawningPrefab;

    private const float GroundOffset = 0f;

    private ECS _ecs;
    private readonly Dictionary<ulong, GameObject> _aliveObjects = new();
    private readonly Dictionary<ulong, GameObject> _respawningObjects = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityDiedEvent>(OnEntityDied);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityRespawnedEvent>(OnEntityRespawned);
    }

    private void OnEntityDied(RespawnableEntityDiedEvent e)
    {
        if (_aliveObjects.TryGetValue(e.EntityId, out GameObject aliveGo))
            aliveGo.SetActive(false);
        if (_respawningObjects.TryGetValue(e.EntityId, out GameObject respawningGo))
            respawningGo.SetActive(true);
    }

    private void OnEntityRespawned(RespawnableEntityRespawnedEvent e)
    {
        if (_aliveObjects.TryGetValue(e.EntityId, out GameObject aliveGo))
            aliveGo.SetActive(true);
        if (_respawningObjects.TryGetValue(e.EntityId, out GameObject respawningGo))
            respawningGo.SetActive(false);
    }

    public void OnEntityAdded(ulong _) { }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_aliveObjects.TryGetValue(entityId, out GameObject alive)) Destroy(alive);
        if (_respawningObjects.TryGetValue(entityId, out GameObject respawning)) Destroy(respawning);
        _aliveObjects.Remove(entityId);
        _respawningObjects.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_aliveObjects.ContainsKey(entityId) || _ecs == null) return;

        var posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return;

        Vector3 worldPos = ToWorldPosition(posStore.GetComponent(entityId));

        if (_alivePrefab != null)
        {
            GameObject alive = Instantiate(_alivePrefab);
            alive.name = $"RespawnableBuilding_Alive_{entityId}";
            alive.transform.position = worldPos;
            _aliveObjects[entityId] = alive;
        }

        if (_respawningPrefab != null)
        {
            GameObject respawning = Instantiate(_respawningPrefab);
            respawning.name = $"RespawnableBuilding_Respawning_{entityId}";
            respawning.transform.position = worldPos;
            respawning.SetActive(false);
            _respawningObjects[entityId] = respawning;
        }
    }

    public void UpdateRenderable(List<ulong> _) { }

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
