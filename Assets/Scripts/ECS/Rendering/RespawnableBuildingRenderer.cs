using System.Collections.Generic;
using UnityEngine;

// Renders a respawnable resource building with two states. On RespawnableEntityDiedEvent,
// switches to _respawningPrefab. On RespawnableEntityRespawnedEvent, switches back to
// _alivePrefab. Both GameObjects are instantiated on activation; no per-frame querying.
public class RespawnableBuildingRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _alivePrefab;
    [SerializeField] private GameObject _respawningPrefab;
    // If non-empty, one is chosen at random per entity instead of _alivePrefab/_respawningPrefab.
    // The same random index is used for both lists, so e.g. variant 0's alive and respawning
    // visuals are always paired together on a given entity.
    [SerializeField] private GameObject[] _aliveVariants;
    [SerializeField] private GameObject[] _respawningVariants;
    [SerializeField] private bool _randomRotation;
    [SerializeField] private float _minScale = 1f;
    [SerializeField] private float _maxScale = 1f;

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

        int variantCount = Mathf.Max(_aliveVariants?.Length ?? 0, _respawningVariants?.Length ?? 0);
        int variantIndex = variantCount > 0 ? Random.Range(0, variantCount) : 0;
        Quaternion rotation = _randomRotation ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity;
        float scale = Random.Range(_minScale, _maxScale);

        GameObject alivePrefab = ChooseVariant(_aliveVariants, variantIndex, _alivePrefab);
        if (alivePrefab != null)
        {
            GameObject alive = Instantiate(alivePrefab);
            alive.name = $"RespawnableBuilding_Alive_{entityId}";
            alive.transform.position = worldPos;
            alive.transform.rotation = rotation;
            alive.transform.localScale *= scale;
            _aliveObjects[entityId] = alive;
        }

        GameObject respawningPrefab = ChooseVariant(_respawningVariants, variantIndex, _respawningPrefab);
        if (respawningPrefab != null)
        {
            GameObject respawning = Instantiate(respawningPrefab);
            respawning.name = $"RespawnableBuilding_Respawning_{entityId}";
            respawning.transform.position = worldPos;
            respawning.transform.rotation = rotation;
            respawning.transform.localScale *= scale;
            respawning.SetActive(false);
            _respawningObjects[entityId] = respawning;
        }
    }

    public void UpdateRenderable(List<ulong> _) { }

    private static GameObject ChooseVariant(GameObject[] variants, int index, GameObject fallback)
        => variants != null && variants.Length > 0 ? variants[index % variants.Length] : fallback;

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
