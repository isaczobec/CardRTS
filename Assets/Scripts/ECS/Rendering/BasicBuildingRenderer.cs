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
    // If non-empty, one of these is chosen at random per entity instead of _prefab.
    [SerializeField] private GameObject[] _prefabVariants;
    [SerializeField] private bool _randomRotation;
    [SerializeField] private float _minScale = 1f;
    [SerializeField] private float _maxScale = 1f;

    [Header("Audio")]
    [SerializeField] private string _takeDamageSoundName;
    [SerializeField] private string _destroyedSoundName;

    private const float GroundOffset = 0f;

    private ECS _ecs;
    private readonly Dictionary<ulong, GameObject> _objects = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out GameObject go))
            PlaySoundAt(_takeDamageSoundName, go.transform.position);
    }

    private void PlaySoundAt(string soundName, Vector3 position)
    {
        if (string.IsNullOrEmpty(soundName) || AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShotAtPosition(soundName, position);
    }

    public void OnEntityAdded(ulong entityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same as troops.
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out GameObject go))
        {
            PlaySoundAt(_destroyedSoundName, go.transform.position);
            Destroy(go);
        }
        _objects.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _ecs == null) return;

        GameObject prefab = ChoosePrefab();
        if (prefab == null) return;

        var posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return;

        GameObject go = Instantiate(prefab);
        go.name = $"Building_{entityId}";
        go.transform.position = ToWorldPosition(posStore.GetComponent(entityId));

        if (_randomRotation)
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        float scale = Random.Range(_minScale, _maxScale);
        go.transform.localScale *= scale;

        _objects[entityId] = go;
    }

    // Buildings don't move — nothing to do per frame.
    public void UpdateRenderable(List<ulong> entityIds) { }

    // No renderers to expose — see IComponentRenderer.GetRenderers.
    public IReadOnlyList<Renderer> GetRenderers(ulong entityId) => null;

    private GameObject ChoosePrefab()
    {
        if (_prefabVariants != null && _prefabVariants.Length > 0)
            return _prefabVariants[Random.Range(0, _prefabVariants.Length)];
        return _prefab;
    }

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
