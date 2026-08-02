using System.Collections.Generic;
using UnityEngine;

// Spawns a world-space health bar for every troop as it activates, keeps it positioned
// above its entity, and updates its health off DamageRequest's SubscribeExecuted callback
// (fires right after Execute mutates HealthComponent — see RequestManager) rather than
// DamageDealtEvent/ServerFlagEvents, so a bar reflects predicted damage immediately instead
// of waiting on server confirmation. This is safe to predict even though a mispredicted
// DamageRequest can end up replayed more than once during reconciliation (unlike a one-shot
// sound/animation trigger): SetHealth just re-applies whatever HealthComponent.CurrentHealth
// currently reads as, so a redundant call is harmless — it settles on the correct value
// either way. Bars are torn down
// on EntityDeletedEvent only — NOT TroopDiedEvent, which fires on every death including a
// respawnable entity's (RespawnSystem vetoes deletion for those, so IsDead flips but the
// entity survives to be revived in place — see RespawnableEntityDiedEvent/
// RespawnableEntityRespawnedEvent, which hide/show the bar for exactly that case). Mirrors
// SelectionManager's lifecycle pattern.
public class HealthBarManager : Singleton<HealthBarManager>
{
    [SerializeField] private GameObject _healthBarPrefab;

    [Header("Owner Colors")]
    // Tints HealthBarPrefab's own _MiddleColor shader property — mirrors SelectionManager's
    // own friendly/neutral/enemy coloring, just with distinct defaults here since a health
    // bar isn't already carrying a colored ring around the troop the way a selection
    // indicator is.
    [SerializeField] private Color _friendlyColor = Color.green;
    [SerializeField] private Color _neutralColor = Color.yellow;
    [SerializeField] private Color _enemyColor = Color.red;

    // Fallback for StatsQuery.GetSpeed below, mirrors BasicTroopRenderer's own DefaultSpeed.
    private const int DefaultSpeed = 100;

    private readonly Dictionary<ulong, HealthBarPrefab> _healthBars = new();
    private readonly TickPositionInterpolator _interpolator = new();

    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<HealthComponent> _healthStore;
    private ComponentStore<MovableComponent> _movableStore;
    private ComponentStore<TroopComponent> _troopStore;

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnTroopActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityDiedEvent>(OnRespawnableEntityDied);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityRespawnedEvent>(OnRespawnableEntityRespawned);

        _ecs = TickManager.instance.ActiveECS;
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _healthStore = _ecs.GetComponentStore<HealthComponent>();
        _movableStore = _ecs.GetComponentStore<MovableComponent>();
        _troopStore = _ecs.GetComponentStore<TroopComponent>();

        _ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageRequestExecuted);
        _ecs.Requests.SubscribeExecuted<HealRequest>(OnHealRequestExecuted);
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        foreach (var kvp in _healthBars)
        {
            ulong entityId = kvp.Key;
            if (!_positionStore.HasComponent(entityId)) continue;

            PositionComponent pos = _positionStore.GetComponent(entityId);
            ApplyPosition(entityId, pos, kvp.Value);
        }
    }

    private void OnTroopActivated(EntityActivatedEvent e)
    {
        if (_healthBars.ContainsKey(e.EntityId)) return;
        if (!_healthStore.HasComponent(e.EntityId)) return;
        if (!_positionStore.HasComponent(e.EntityId)) return;

        GameObject go = Instantiate(_healthBarPrefab, transform);
        go.name = $"HealthBar_{e.EntityId}";
        HealthBarPrefab bar = go.GetComponent<HealthBarPrefab>();
        _healthBars[e.EntityId] = bar;

        ApplyPosition(e.EntityId, _positionStore.GetComponent(e.EntityId), bar);
        ApplyHealth(e.EntityId, bar);
        ApplyOwnerColor(e.EntityId, bar);

        // An entity can already be dead the moment it activates (e.g. a world-gen resource
        // node spawned dead-on-spawn — see EntitySpawnAction.SpawnSoulstoneNode) — that never
        // raises RespawnableEntityDiedEvent (nothing ever transitioned from alive to dead), so
        // the bar defaulting to visible above needs correcting for that case right here.
        bool isDead = _troopStore != null && _troopStore.HasComponent(e.EntityId) && _troopStore.GetComponent(e.EntityId).IsDead;
        if (isDead)
            bar.gameObject.SetActive(false);
    }

    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyHealthBar(e.EntityId);

    private void OnDamageRequestExecuted(DamageRequest request, ECS ecs)
    {
        if (!_healthBars.TryGetValue(request.EntityId, out HealthBarPrefab bar)) return;
        ApplyHealth(request.EntityId, bar);
    }

    // Same predicted-immediately reasoning as OnDamageRequestExecuted above, just for
    // HealRequest instead.
    private void OnHealRequestExecuted(HealRequest request, ECS ecs)
    {
        if (!_healthBars.TryGetValue(request.EntityId, out HealthBarPrefab bar)) return;
        ApplyHealth(request.EntityId, bar);
    }

    private void ApplyHealth(ulong entityId, HealthBarPrefab bar)
    {
        if (!_healthStore.HasComponent(entityId)) return;

        int currentHealth = _healthStore.GetComponent(entityId).CurrentHealth;
        int maxHealth = StatsQuery.GetMaxHealth(_ecs, entityId, currentHealth);
        bar.SetHealth(currentHealth, maxHealth);
    }

    // Called once, right when the bar is created — ownership never changes after a troop
    // spawns, so this never needs revisiting the way ApplyHealth/ApplyPosition do every tick/
    // frame. Mirrors SelectionManager.GetUnselectedColor's own friendly/neutral/enemy split.
    private void ApplyOwnerColor(ulong entityId, HealthBarPrefab bar)
    {
        if (_troopStore == null || !_troopStore.HasComponent(entityId)) return;

        ushort owner = _troopStore.GetComponent(entityId).OwnerPlayerId;
        Color color;
        if (owner == LocalPlayerId()) color = _friendlyColor;
        else if (owner == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) color = _neutralColor;
        else color = _enemyColor;

        bar.SetOwnerColor(color);
    }

    private ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    // Same closed-loop "chase the true tick position at the entity's own Speed stat" style
    // BasicTroopRenderer/VariedAttackTroopRenderer use for the 3D model itself (see
    // TickPositionInterpolator's own doc comment on why that's self-correcting where the
    // old strict-lerp-only approach could drift) — falls back to the plain strict-lerp
    // overload while not moving, teleported, or displaced (a knockback's velocity isn't
    // bounded by the Speed stat, so chasing at Speed could lag behind it — same guard
    // BasicTroopRenderer's own branch uses).
    private void ApplyPosition(ulong entityId, PositionComponent pos, HealthBarPrefab bar)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        Vector3 worldPos = new Vector3(pos.X, height, pos.Y);
        bool isMoving = _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).IsMoving;
        bool teleported = _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).TeleportedTick == _ecs.CurrentSimulationTick;
        bool displaced = _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).IsDisplaced;

        Vector3 interpolated;
        if (isMoving && !displaced)
        {
            float speedWorldUnitsPerSecond = StatsQuery.GetSpeed(_ecs, entityId, DefaultSpeed) / StatsQuery.SpeedScale;
            interpolated = _interpolator.Update(entityId, worldPos, isMoving, teleported, speedWorldUnitsPerSecond);
        }
        else
        {
            interpolated = _interpolator.Update(entityId, worldPos, isMoving, teleported);
        }

        bar.transform.position = interpolated + bar.WorldOffset;
    }

    private void OnRespawnableEntityDied(RespawnableEntityDiedEvent e)
    {
        if (_healthBars.TryGetValue(e.EntityId, out HealthBarPrefab bar))
            bar.gameObject.SetActive(false);
    }

    private void OnRespawnableEntityRespawned(RespawnableEntityRespawnedEvent e)
    {
        if (!_healthBars.TryGetValue(e.EntityId, out HealthBarPrefab bar)) return;
        bar.gameObject.SetActive(true);
        ApplyHealth(e.EntityId, bar);
    }

    private void DestroyHealthBar(ulong entityId)
    {
        if (!_healthBars.TryGetValue(entityId, out HealthBarPrefab bar)) return;
        Destroy(bar.gameObject);
        _healthBars.Remove(entityId);
        _interpolator.Remove(entityId);
    }
}
