using UnityEngine;

// Handler for floating combat/economy text — spawns a FloatingTextSpawner instance at the
// relevant world position for damage numbers (DamageDealtEvent, if the damaged entity has
// a PositionComponent) and resource gains (ResourcesChangedEvent, only when it carries a
// valid world location — see ResourcesAdded.NO_WORLD_LOCATION; spends and untargeted
// passive generation never set one, so they're silently skipped here).
//
// Purely event-driven and, like SelectionManager, sits outside the ECS system list
// entirely — it's never registered as an ISystem, only subscribes to the two flag events
// above via TickManager.ServerFlagEvents.
public class FloatingTextManager : Singleton<FloatingTextManager>
{
    [SerializeField] private FloatingTextSpawner _floatingTextPrefab;
    // Applied as a random XZ offset to every spawn position, so simultaneous hits/gains at
    // the same spot don't render exactly on top of one another.
    [SerializeField] private float _spawnJitterRadius = 0.3f;

    [Header("Damage")]
    [SerializeField] private Color _damageColor = Color.red;
    [SerializeField] private Sprite _damageIcon;

    [Header("Resource Gain")]
    [SerializeField] private Color _resourceGainColor = Color.green;
    [SerializeField] private Sprite _woodIcon;
    [SerializeField] private Sprite _stoneIcon;
    [SerializeField] private Sprite _metalIcon;
    [SerializeField] private Sprite _gemsIcon;
    [SerializeField] private Sprite _soulstonesIcon;
    [SerializeField] private Sprite _goldIcon;

    private ComponentStore<PositionComponent> _positionStore;

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
        TickManager.instance.ServerFlagEvents.Subscribe<ResourcesChangedEvent>(OnResourcesChanged);

        _positionStore = TickManager.instance.ActiveECS.GetComponentStore<PositionComponent>();
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_positionStore == null || !_positionStore.HasComponent(e.EntityId)) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        Spawn(WorldPositionFor(pos.X, pos.Y), $"{e.Amount}", _damageColor, _damageIcon);
    }

    private void OnResourcesChanged(ResourcesChangedEvent e)
    {
        if (e.X == ResourcesAdded.NO_WORLD_LOCATION || e.Y == ResourcesAdded.NO_WORLD_LOCATION) return;
        if (e.ClientId != LocalPlayerId()) return;

        Vector3 worldPos = WorldPositionFor(e.X, e.Y);
        SpawnResourceGain(worldPos, e.WoodDelta, _woodIcon);
        SpawnResourceGain(worldPos, e.StoneDelta, _stoneIcon);
        SpawnResourceGain(worldPos, e.MetalDelta, _metalIcon);
        SpawnResourceGain(worldPos, e.GemsDelta, _gemsIcon);
        SpawnResourceGain(worldPos, e.SoulstonesDelta, _soulstonesIcon);
        SpawnResourceGain(worldPos, e.GoldDelta, _goldIcon);
    }

    // Only gains ("resource generated" numbers) show floating text — a spend's feedback
    // already lives on the card UI itself (see CardGameObject's affordability coloring).
    private void SpawnResourceGain(Vector3 worldPos, float delta, Sprite icon)
    {
        if (delta <= 0f) return;
        Spawn(worldPos, $"+{Mathf.RoundToInt(delta)}", _resourceGainColor, icon);
    }

    private void Spawn(Vector3 worldPos, string text, Color color, Sprite icon)
    {
        if (_floatingTextPrefab == null) return;

        Vector2 jitter = Random.insideUnitCircle * _spawnJitterRadius;
        worldPos += new Vector3(jitter.x, 0f, jitter.y);

        FloatingTextSpawner instance = Instantiate(_floatingTextPrefab, worldPos, Quaternion.identity);
        instance.Setup(text, color, icon);
    }

    private static Vector3 WorldPositionFor(float x, float y)
    {
        float height = WorldManager.instance.Handler.GetHeight((ushort)x, (ushort)y);
        return new Vector3(x, height + 1f, y);
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
}
