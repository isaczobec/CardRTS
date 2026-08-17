using System.Collections.Generic;
using UnityEngine;

// Renders a building whose TroopComponent.OwnerPlayerId can change at any time during the
// match (currently only CapturableBuildingComponent buildings — see CapturableBuildingSystem's
// instant-capture-on-death) with a different prefab depending on its CURRENT ownership
// relative to the local player: neutral, friendly (owned by the local player), or enemy (owned
// by anyone else). Register an instance with RenderableManager for
// RenderableType.CapturableBuilding, same as BasicBuildingRenderer/RespawnableBuildingRenderer
// are registered for their own types.
//
// Unlike RespawnableBuildingRenderer's alive/dead swap (both variants pre-instantiated,
// toggled active on a dedicated event), a capture is a comparatively rare event and there are
// three states rather than two, so this instead destroys and re-instantiates the single active
// GameObject whenever the resolved category actually changes — checked every frame in
// UpdateRenderable rather than off a dedicated event, since ownership can flip from ordinary
// combat damage rather than a single scripted transition.
public class OwnershipBuildingRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _neutralPrefab;
    [SerializeField] private GameObject _friendlyPrefab;
    [SerializeField] private GameObject _enemyPrefab;
    [SerializeField] private bool _randomRotation;
    [SerializeField] private float _minScale = 1f;
    [SerializeField] private float _maxScale = 1f;

    [Header("Audio")]
    [SerializeField] private string _takeDamageSoundName;
    // Plays whenever an already-active building's resolved ownership category changes —
    // covers both "captured from neutral" and "recaptured from another player".
    [SerializeField] private string _capturedSoundName;

    private const float GroundOffset = 0f;

    private enum OwnershipCategory { Neutral, Friendly, Enemy }

    private ECS _ecs;
    private ComponentStore<TroopComponent> _troopStore;
    private readonly Dictionary<ulong, GameObject> _objects = new();
    private readonly Dictionary<ulong, OwnershipCategory> _currentCategory = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _troopStore = ecs.GetComponentStore<TroopComponent>();
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
        // No visual yet — spawned on activation (see OnEntityActivated), same as
        // BasicBuildingRenderer/RespawnableBuildingRenderer.
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out GameObject go))
            Destroy(go);
        _objects.Remove(entityId);
        _currentCategory.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _ecs == null) return;

        var posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return;

        OwnershipCategory category = ResolveCategory(entityId);
        GameObject prefab = PrefabFor(category);
        if (prefab == null) return;

        Quaternion rotation = _randomRotation ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity;
        float scale = Random.Range(_minScale, _maxScale);

        GameObject go = Instantiate(prefab);
        go.name = $"OwnershipBuilding_{entityId}";
        go.transform.position = ToWorldPosition(posStore.GetComponent(entityId));
        go.transform.rotation = rotation;
        go.transform.localScale *= scale;

        _objects[entityId] = go;
        _currentCategory[entityId] = category;
    }

    // Ownership can change at any time (captured/recaptured via ordinary combat — see
    // CapturableBuildingSystem) rather than on a single scripted event, so every active
    // building's category is re-resolved and compared every frame instead of reacting to a
    // dedicated event.
    public void UpdateRenderable(List<ulong> entityIds)
    {
        if (_ecs == null || _troopStore == null) return;

        foreach (ulong entityId in entityIds)
        {
            if (!_objects.TryGetValue(entityId, out GameObject oldGo)) continue;

            OwnershipCategory category = ResolveCategory(entityId);
            if (_currentCategory.TryGetValue(entityId, out OwnershipCategory previous) && previous == category) continue;

            GameObject prefab = PrefabFor(category);
            if (prefab == null) continue;

            GameObject go = Instantiate(prefab);
            go.name = $"OwnershipBuilding_{entityId}";
            go.transform.SetPositionAndRotation(oldGo.transform.position, oldGo.transform.rotation);
            go.transform.localScale = oldGo.transform.localScale;

            Destroy(oldGo);
            _objects[entityId] = go;
            _currentCategory[entityId] = category;

            PlaySoundAt(_capturedSoundName, go.transform.position);
        }
    }

    // No renderers to expose — see IComponentRenderer.GetRenderers.
    public IReadOnlyList<Renderer> GetRenderers(ulong entityId) => null;

    private OwnershipCategory ResolveCategory(ulong entityId)
    {
        if (!_troopStore.HasComponent(entityId)) return OwnershipCategory.Neutral;

        ushort ownerId = _troopStore.GetComponent(entityId).OwnerPlayerId;
        if (ownerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return OwnershipCategory.Neutral;

        ushort localPlayerId = NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
        return ownerId == localPlayerId ? OwnershipCategory.Friendly : OwnershipCategory.Enemy;
    }

    private GameObject PrefabFor(OwnershipCategory category) => category switch
    {
        OwnershipCategory.Friendly => _friendlyPrefab,
        OwnershipCategory.Enemy => _enemyPrefab,
        _ => _neutralPrefab,
    };

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
