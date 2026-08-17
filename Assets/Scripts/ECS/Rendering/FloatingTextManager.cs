using UnityEngine;

// Handler for floating combat/economy/card text — spawns a FloatingTextSpawner instance at
// the relevant world position for damage numbers (DamageDealtEvent, if the damaged entity has
// a PositionComponent), resource gains (ResourcesChangedEvent, only when it carries a valid
// world location — see ResourcesAdded.NO_WORLD_LOCATION; spends and untargeted passive
// generation never set one, so they're silently skipped here), and a played card's name/
// artwork once its spawn actually activates (EntityActivatedEvent, only for an entity tagged
// SpawnedByCardComponent — see OnEntityActivated).
//
// Purely event-driven and, like SelectionManager, sits outside the ECS system list
// entirely — it's never registered as an ISystem, only subscribes to flag events via
// TickManager.ServerFlagEvents. That's also what makes the card-name popup visible to every
// client rather than just whoever played it: EntityActivatedEvent is fired once by
// ActivationSystem on the authoritative server tick and networked to every client (see
// FlagEvent.ShouldNetwork's default) — this never subscribes to a local/predicted ECS's own
// FlagEvents, only the server-confirmed channel every other manager here already uses, so it
// fires exactly once per activation on every machine, not once per client's own prediction.
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

    [Header("Card Played")]
    [SerializeField] private Color _cardPlayedColor = Color.white;

    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<SpawnedByCardComponent> _spawnedByCardStore;
    private ComponentStore<CardComponent> _cardStore;

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
        TickManager.instance.ServerFlagEvents.Subscribe<ResourcesChangedEvent>(OnResourcesChanged);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnEntityActivated);

        ECS ecs = TickManager.instance.ActiveECS;
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _spawnedByCardStore = ecs.GetComponentStore<SpawnedByCardComponent>();
        _cardStore = ecs.GetComponentStore<CardComponent>();
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

    // Only entities SpawnAtPointCardPlaySystem tagged SpawnedByCardComponent (a Troop/
    // Building card's own spawn(s) — see that component's doc comment) trigger this; a
    // dev-spawned test troop (SpawnTroopSystem) or a Spell card's spawn (never tagged) is
    // silently skipped, same as no CardComponent/CardRegistry entry being found for whatever
    // reason. A card whose OnPlayed spawns several entities at once (e.g. SkeletonsCard's 8)
    // activates each independently, so this can legitimately fire more than once per play —
    // no attempt is made to dedupe that here.
    private void OnEntityActivated(EntityActivatedEvent e)
    {
        if (_spawnedByCardStore == null || _cardStore == null) return;
        if (!_spawnedByCardStore.HasComponent(e.EntityId)) return;

        ulong cardEntityId = _spawnedByCardStore.GetComponent(e.EntityId).CardEntityId;
        if (cardEntityId == 0 || !_cardStore.HasComponent(cardEntityId)) return;

        CardType type = _cardStore.GetComponent(cardEntityId).Type;
        if (!CardRegistry.TryGet(type, out Card definition)) return;

        Sprite icon = null;
        if (ImageRegistry.instance != null)
            ImageRegistry.instance.TryGet(definition.ImageName, out icon);

        Spawn(WorldPositionFor(e.X, e.Y), definition.Title, _cardPlayedColor, icon);
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
