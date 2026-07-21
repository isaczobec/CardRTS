using System.Collections.Generic;
using UnityEngine;

// Shows a flat ground disc around every friendly building (and, for cards that opt in via
// Card.AllowsFriendlyTroopRange, every friendly physical troop) while the player has a
// card selected or being dragged that requires playing within range of one — sized to
// that specific card's effective range (BuildingRangeHelper.GetEffectiveRange for
// buildings; Card.MaxDistanceFromFriendlyTroop directly for troops, since troops have no
// multiplier/bonus equivalent to BuildingComponent). Hidden whenever no card is active, or
// the active card doesn't apply to that indicator's kind.
//
// Separate from the ECS architecture, like SelectionManager — never registered as an
// ISystem, just polls the live ECS each frame. Buildings never move, so their indicators
// are positioned once at spawn; troops do move, so their indicators are re-positioned
// every frame via a TickPositionInterpolator, mirroring SelectionManager's selection rings.
public class CardRangeIndicatorManager : Singleton<CardRangeIndicatorManager>
{
    [SerializeField] private RangeIndicatorPrefab _indicatorPrefab;
    [SerializeField] private Color _color = new Color(0f, 1f, 0f, 0.25f);

    private ECS _ecs;
    private ComponentStore<BuildingComponent> _buildingStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<SelectableComponent> _selectableStore;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<MovableComponent> _movableStore;

    private readonly Dictionary<ulong, RangeIndicatorPrefab> _buildingIndicators = new();
    private readonly Dictionary<ulong, RangeIndicatorPrefab> _troopIndicators = new();
    private readonly TickPositionInterpolator _troopInterpolator = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnEntityActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<BuildingComponent>>(OnBuildingRemoved);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<TroopComponent>>(OnTroopRemoved);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);

        _ecs = TickManager.instance.ActiveECS;
        _buildingStore = _ecs.GetComponentStore<BuildingComponent>();
        _troopStore = _ecs.GetComponentStore<TroopComponent>();
        _selectableStore = _ecs.GetComponentStore<SelectableComponent>();
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _movableStore = _ecs.GetComponentStore<MovableComponent>();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        Card activeCard = CardHandRenderer.instance != null ? CardHandRenderer.instance.ResolveActiveCard() : null;
        bool showBuildings = activeCard != null && activeCard.RequiresFriendlyBuildingRange();
        bool showTroops = activeCard != null && activeCard.AllowsFriendlyTroopRange() && activeCard.MaxDistanceFromFriendlyTroop > 0f;

        foreach (KeyValuePair<ulong, RangeIndicatorPrefab> kvp in _buildingIndicators)
        {
            ulong buildingId = kvp.Key;
            RangeIndicatorPrefab indicator = kvp.Value;

            if (!showBuildings || !_buildingStore.HasComponent(buildingId))
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            float effectiveRange = BuildingRangeHelper.GetEffectiveRange(_buildingStore.GetComponent(buildingId), activeCard.MaxDistanceFromFriendlyBuilding);
            if (effectiveRange <= 0f)
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            indicator.gameObject.SetActive(true);
            indicator.SetScale(effectiveRange * 2f); // radius -> diameter
        }

        foreach (KeyValuePair<ulong, RangeIndicatorPrefab> kvp in _troopIndicators)
        {
            ulong troopId = kvp.Key;
            RangeIndicatorPrefab indicator = kvp.Value;

            // Keep tracking the troop's position every frame, even while hidden, so the
            // indicator doesn't jump to a stale spot the instant it's shown again.
            if (_positionStore.HasComponent(troopId))
            {
                PositionComponent pos = _positionStore.GetComponent(troopId);
                indicator.transform.position = _troopInterpolator.Update(troopId, WorldPositionFor(pos), IsMoving(troopId));
            }

            if (!showTroops || !_positionStore.HasComponent(troopId))
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            indicator.gameObject.SetActive(true);
            indicator.SetScale(activeCard.MaxDistanceFromFriendlyTroop * 2f); // radius -> diameter
        }
    }

    // ── Indicator lifecycle — one per friendly building/troop, mirroring
    // SelectionManager's EntityActivatedEvent/ComponentRemovedEvent/EntityDeletedEvent
    // pattern. ─────────────────────────────────────────────────────────────────────

    private void OnEntityActivated(EntityActivatedEvent e)
    {
        SetupBuildingIndicator(e.EntityId);
        SetupTroopIndicator(e.EntityId);
    }

    private void SetupBuildingIndicator(ulong entityId)
    {
        if (_buildingIndicators.ContainsKey(entityId)) return;
        if (_buildingStore == null || !_buildingStore.HasComponent(entityId)) return;
        if (_selectableStore == null || !_selectableStore.HasComponent(entityId)) return;
        if (_selectableStore.GetComponent(entityId).OwnerPlayerId != LocalPlayerId()) return;
        if (_positionStore == null || !_positionStore.HasComponent(entityId)) return;
        if (_indicatorPrefab == null) return;

        PositionComponent pos = _positionStore.GetComponent(entityId);
        RangeIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(pos), Quaternion.identity, transform);
        indicator.name = $"RangeIndicator_Building_{entityId}";
        indicator.SetColor(_color);
        indicator.gameObject.SetActive(false);

        _buildingIndicators[entityId] = indicator;
    }

    private void SetupTroopIndicator(ulong entityId)
    {
        if (_troopIndicators.ContainsKey(entityId)) return;
        if (_troopStore == null || !_troopStore.HasComponent(entityId)) return;
        if (!_troopStore.GetComponent(entityId).IsPhysicalTroop) return;
        if (_selectableStore == null || !_selectableStore.HasComponent(entityId)) return;
        if (_selectableStore.GetComponent(entityId).OwnerPlayerId != LocalPlayerId()) return;
        if (_positionStore == null || !_positionStore.HasComponent(entityId)) return;
        if (_indicatorPrefab == null) return;

        PositionComponent pos = _positionStore.GetComponent(entityId);
        RangeIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(pos), Quaternion.identity, transform);
        indicator.name = $"RangeIndicator_Troop_{entityId}";
        indicator.SetColor(_color);
        indicator.gameObject.SetActive(false);

        _troopIndicators[entityId] = indicator;
    }

    private void OnBuildingRemoved(ComponentRemovedEvent<BuildingComponent> e) => DestroyBuildingIndicator(e.EntityId);
    private void OnTroopRemoved(ComponentRemovedEvent<TroopComponent> e) => DestroyTroopIndicator(e.EntityId);

    private void OnEntityDeleted(EntityDeletedEvent e)
    {
        DestroyBuildingIndicator(e.EntityId);
        DestroyTroopIndicator(e.EntityId);
    }

    private void DestroyBuildingIndicator(ulong entityId)
    {
        if (!_buildingIndicators.TryGetValue(entityId, out RangeIndicatorPrefab indicator)) return;
        Destroy(indicator.gameObject);
        _buildingIndicators.Remove(entityId);
    }

    private void DestroyTroopIndicator(ulong entityId)
    {
        if (!_troopIndicators.TryGetValue(entityId, out RangeIndicatorPrefab indicator)) return;
        Destroy(indicator.gameObject);
        _troopIndicators.Remove(entityId);
        _troopInterpolator.Remove(entityId);
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }

    private bool IsMoving(ulong entityId)
        => _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).IsMoving;

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
}
