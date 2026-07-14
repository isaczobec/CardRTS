using System.Collections.Generic;
using UnityEngine;

// Shows a flat ground disc around every friendly building while the player has a card
// selected or being dragged that requires playing within range of one (see
// Card.RequiresFriendlyBuildingRange) — sized to that specific card's effective range per
// building (BuildingRangeHelper.GetEffectiveRange). Hidden whenever no card is active, or
// the active card has unlimited range.
//
// Separate from the ECS architecture, like SelectionManager — never registered as an
// ISystem, just polls the live ECS each frame. Buildings never move, so unlike
// SelectionManager's rings this never needs TickPositionInterpolator; only each
// indicator's visibility/scale changes per frame, driven by CardHandRenderer's selection
// state.
public class CardRangeIndicatorManager : Singleton<CardRangeIndicatorManager>
{
    [SerializeField] private RangeIndicatorPrefab _indicatorPrefab;
    [SerializeField] private Color _color = new Color(0f, 1f, 0f, 0.25f);

    private ECS _ecs;
    private ComponentStore<BuildingComponent> _buildingStore;
    private ComponentStore<SelectableComponent> _selectableStore;
    private ComponentStore<PositionComponent> _positionStore;

    private readonly Dictionary<ulong, RangeIndicatorPrefab> _indicators = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnTroopActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<BuildingComponent>>(OnBuildingRemoved);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);

        _ecs = TickManager.instance.ActiveECS;
        _buildingStore = _ecs.GetComponentStore<BuildingComponent>();
        _selectableStore = _ecs.GetComponentStore<SelectableComponent>();
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        Card activeCard = CardHandRenderer.instance != null ? CardHandRenderer.instance.ResolveActiveCard() : null;
        bool show = activeCard != null && activeCard.RequiresFriendlyBuildingRange();

        foreach (KeyValuePair<ulong, RangeIndicatorPrefab> kvp in _indicators)
        {
            ulong buildingId = kvp.Key;
            RangeIndicatorPrefab indicator = kvp.Value;

            if (!show || !_buildingStore.HasComponent(buildingId))
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
    }

    // ── Indicator lifecycle — one per friendly building, mirroring SelectionManager's
    // EntityActivatedEvent/ComponentRemovedEvent/EntityDeletedEvent pattern. ─────────────

    private void OnTroopActivated(EntityActivatedEvent e)
    {
        if (_indicators.ContainsKey(e.EntityId)) return;
        if (_buildingStore == null || !_buildingStore.HasComponent(e.EntityId)) return;
        if (_selectableStore == null || !_selectableStore.HasComponent(e.EntityId)) return;
        if (_selectableStore.GetComponent(e.EntityId).OwnerPlayerId != LocalPlayerId()) return;
        if (_positionStore == null || !_positionStore.HasComponent(e.EntityId)) return;
        if (_indicatorPrefab == null) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        RangeIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(pos), Quaternion.identity, transform);
        indicator.name = $"RangeIndicator_{e.EntityId}";
        indicator.SetColor(_color);
        indicator.gameObject.SetActive(false);

        _indicators[e.EntityId] = indicator;
    }

    private void OnBuildingRemoved(ComponentRemovedEvent<BuildingComponent> e) => DestroyIndicator(e.EntityId);
    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyIndicator(e.EntityId);

    private void DestroyIndicator(ulong entityId)
    {
        if (!_indicators.TryGetValue(entityId, out RangeIndicatorPrefab indicator)) return;
        Destroy(indicator.gameObject);
        _indicators.Remove(entityId);
    }

    // Buildings never move, so this is only ever computed once, at spawn — unlike
    // SelectionManager's troops/rings, no per-frame position tracking is needed.
    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
}
