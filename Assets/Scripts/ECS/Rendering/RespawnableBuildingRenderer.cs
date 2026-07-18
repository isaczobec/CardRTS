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

    [Header("Audio")]
    [SerializeField] private string _takeDamageSoundName;
    [SerializeField] private string _diedSoundName;
    [SerializeField] private string _respawnedSoundName;

    private const float GroundOffset = 0f;

    private ECS _ecs;
    private readonly Dictionary<ulong, GameObject> _aliveObjects = new();
    private readonly Dictionary<ulong, GameObject> _respawningObjects = new();

    private ComponentStore<TroopComponent> _troopStore;

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _troopStore = ecs.GetComponentStore<TroopComponent>();
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityDiedEvent>(OnEntityDied);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityRespawnedEvent>(OnEntityRespawned);
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    private void OnEntityDied(RespawnableEntityDiedEvent e)
    {
        if (_aliveObjects.TryGetValue(e.EntityId, out GameObject aliveGo))
        {
            aliveGo.SetActive(false);
            PlaySoundAt(_diedSoundName, aliveGo.transform.position);
        }
        if (_respawningObjects.TryGetValue(e.EntityId, out GameObject respawningGo))
            respawningGo.SetActive(true);
    }

    private void OnEntityRespawned(RespawnableEntityRespawnedEvent e)
    {
        if (_aliveObjects.TryGetValue(e.EntityId, out GameObject aliveGo))
        {
            aliveGo.SetActive(true);
            PlaySoundAt(_respawnedSoundName, aliveGo.transform.position);
        }
        if (_respawningObjects.TryGetValue(e.EntityId, out GameObject respawningGo))
            respawningGo.SetActive(false);
    }

    // Only while alive — a respawning (dead) building isn't a valid damage target, so
    // there's no matching _aliveObjects entry for the lookup to find in that state anyway.
    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_aliveObjects.TryGetValue(e.EntityId, out GameObject aliveGo))
            PlaySoundAt(_takeDamageSoundName, aliveGo.transform.position);
    }

    private void PlaySoundAt(string soundName, Vector3 position)
    {
        if (string.IsNullOrEmpty(soundName) || AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShotAtPosition(soundName, position);
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

        // An entity can already be dead the moment it activates (e.g. a world-gen resource
        // node spawned dead-on-spawn with a long initial respawn timer — see
        // EntitySpawnAction.SpawnSoulstoneNode) — that never raises RespawnableEntityDiedEvent
        // (nothing ever transitioned from alive to dead), so the initial alive/respawning
        // visibility set above needs correcting for that case right here instead.
        bool isDead = _troopStore != null && _troopStore.HasComponent(entityId) && _troopStore.GetComponent(entityId).IsDead;
        if (isDead)
        {
            if (_aliveObjects.TryGetValue(entityId, out GameObject aliveGo)) aliveGo.SetActive(false);
            if (_respawningObjects.TryGetValue(entityId, out GameObject respawningGo)) respawningGo.SetActive(true);
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
