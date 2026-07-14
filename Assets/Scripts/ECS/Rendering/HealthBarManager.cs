using System.Collections.Generic;
using UnityEngine;

// Spawns a world-space health bar for every troop as it activates, keeps it positioned
// above its entity, and updates its fill amount off DamageDealtEvent. Bars are torn down
// on TroopDiedEvent/EntityDeletedEvent. Mirrors SelectionManager's lifecycle pattern.
public class HealthBarManager : Singleton<HealthBarManager>
{
    [SerializeField] private GameObject _healthBarPrefab;

    private readonly Dictionary<ulong, HealthBarPrefab> _healthBars = new();
    private readonly TickPositionInterpolator _interpolator = new();

    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<HealthComponent> _healthStore;
    private ComponentStore<MovableComponent> _movableStore;

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnTroopActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<TroopDiedEvent>(OnTroopRemoved);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityDiedEvent>(OnRespawnableEntityDied);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityRespawnedEvent>(OnRespawnableEntityRespawned);

        _ecs = TickManager.instance.ActiveECS;
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _healthStore = _ecs.GetComponentStore<HealthComponent>();
        _movableStore = _ecs.GetComponentStore<MovableComponent>();
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
    }

    private void OnTroopRemoved(TroopDiedEvent e) => DestroyHealthBar(e.EntityId);
    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyHealthBar(e.EntityId);

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (!_healthBars.TryGetValue(e.EntityId, out HealthBarPrefab bar)) return;
        ApplyHealth(e.EntityId, bar);
    }

    private void ApplyHealth(ulong entityId, HealthBarPrefab bar)
    {
        if (!_healthStore.HasComponent(entityId)) return;

        int currentHealth = _healthStore.GetComponent(entityId).CurrentHealth;
        int maxHealth = StatsQuery.GetMaxHealth(_ecs, entityId, currentHealth);
        bar.SetFillAmount(maxHealth > 0 ? (float)currentHealth / maxHealth : 0f);
    }

    private void ApplyPosition(ulong entityId, PositionComponent pos, HealthBarPrefab bar)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        Vector3 worldPos = new Vector3(pos.X, height, pos.Y);
        bool isMoving = _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).currentMovementMode != MovementMode.NotMoving;

        bar.transform.position = _interpolator.Update(entityId, worldPos, isMoving) + bar.WorldOffset;
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
