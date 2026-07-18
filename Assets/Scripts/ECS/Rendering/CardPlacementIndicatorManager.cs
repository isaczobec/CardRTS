using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows placement-preview prefabs while a card with a non-empty IndicatorPrefabName is
/// selected or being dragged (see CardHandRenderer.ResolveActiveCard):
///  - SpawnAtPointCard: a single prefab that follows the cursor (unchanged behavior).
///  - MultiPointCard: one prefab per point — frozen in place for each point already
///    clicked (CardHandRenderer.ArmedMultiPointPoints), plus one live prefab following the
///    cursor for the next point not yet placed — and, if the card opts in
///    (MultiPointCard.RenderBetweenPoints), one additional prefab per consecutive pair,
///    positioned at their midpoint and rotated (around Y only, ignoring height) to face
///    from the first to the second, every frame.
/// Hidden whenever no such card is active, or the ground raycast fails (e.g. cursor off the
/// horizon).
///
/// Separate from the ECS architecture, like SelectionManager/CardRangeIndicatorManager —
/// purely reactive to CardHandRenderer's selection state and the live cursor position, no
/// ECS event subscriptions needed.
/// </summary>
public class CardPlacementIndicatorManager : Singleton<CardPlacementIndicatorManager>
{
    // ── SpawnAtPointCard (single indicator) ─────────────────────────────────────
    private string _currentIndicatorName;
    private GameObject _currentIndicator;

    // ── MultiPointCard (one indicator per point, plus optional between-indicators) ─────
    private ulong _multiPointCardEntityId;
    private readonly List<GameObject> _pointIndicators = new List<GameObject>();
    private readonly List<GameObject> _betweenIndicators = new List<GameObject>();

    public void Initialize() { }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        Card activeCard = CardHandRenderer.instance?.ResolveActiveCard();

        if (activeCard is MultiPointCard multiPointCard)
        {
            SetIndicatorActive(false); // hide any leftover single-indicator instance
            UpdateMultiPointIndicators(multiPointCard);
            return;
        }

        ClearMultiPointIndicators();
        UpdateSinglePointIndicator(activeCard as SpawnAtPointCard);
    }

    // ── SpawnAtPointCard ─────────────────────────────────────────────────────────

    private void UpdateSinglePointIndicator(SpawnAtPointCard activeCard)
    {
        string indicatorName = activeCard?.IndicatorPrefabName;
        if (string.IsNullOrEmpty(indicatorName))
        {
            SetIndicatorActive(false);
            return;
        }

        if (indicatorName != _currentIndicatorName)
            SwapIndicator(indicatorName, activeCard);

        if (_currentIndicator == null) return;

        if (TileSpaceMouse.TryGetPosition(out float x, out float y))
        {
            SetIndicatorActive(true);
            _currentIndicator.transform.position = WorldPositionFor(x, y);
        }
        else
        {
            SetIndicatorActive(false);
        }
    }

    // Only re-instantiates when the resolved name actually changes (e.g. switching between
    // two selected cards with different indicators) — repeatedly reselecting the same card
    // just keeps reusing the existing instance.
    private void SwapIndicator(string indicatorName, SpawnAtPointCard card)
    {
        if (_currentIndicator != null)
        {
            Destroy(_currentIndicator);
            _currentIndicator = null;
        }

        _currentIndicatorName = indicatorName;

        if (IndicatorPrefabRegistry.instance == null || !IndicatorPrefabRegistry.instance.TryGet(indicatorName, out GameObject prefab))
            return;

        _currentIndicator = Instantiate(prefab, transform);
        _currentIndicator.name = $"CardPlacementIndicator_{indicatorName}";
        card?.OnIndicatorSpawned(_currentIndicator);
    }

    private void SetIndicatorActive(bool active)
    {
        if (_currentIndicator != null)
            _currentIndicator.SetActive(active);
    }

    // ── MultiPointCard ───────────────────────────────────────────────────────────

    private void UpdateMultiPointIndicators(MultiPointCard card)
    {
        ulong activeEntityId = CardHandRenderer.instance.ActiveCardEntityId;
        IReadOnlyList<Vector2> placedPoints = CardHandRenderer.instance.ArmedMultiPointPoints;

        // A different card (or the same card starting a fresh sequence) than what we last
        // built for — start over rather than trying to reconcile partial state.
        if (activeEntityId != _multiPointCardEntityId || placedPoints.Count > card.PointCount)
        {
            ClearMultiPointIndicators();
            _multiPointCardEntityId = activeEntityId;
        }

        if (string.IsNullOrEmpty(card.IndicatorPrefabName)) return;
        if (IndicatorPrefabRegistry.instance == null || !IndicatorPrefabRegistry.instance.TryGet(card.IndicatorPrefabName, out GameObject prefab)) return;

        float mx = 0f, my = 0f;
        bool haveLivePoint = placedPoints.Count < card.PointCount && TileSpaceMouse.TryGetPosition(out mx, out my);
        int visibleCount = placedPoints.Count + (haveLivePoint ? 1 : 0);

        EnsurePointIndicatorCount(visibleCount, prefab, card);

        for (int i = 0; i < placedPoints.Count && i < _pointIndicators.Count; i++)
            _pointIndicators[i].transform.position = WorldPositionFor(placedPoints[i].x, placedPoints[i].y);

        if (haveLivePoint && placedPoints.Count < _pointIndicators.Count)
            _pointIndicators[placedPoints.Count].transform.position = WorldPositionFor(mx, my);

        for (int i = 0; i < _pointIndicators.Count; i++)
            _pointIndicators[i].SetActive(i < visibleCount);

        if (card.RenderBetweenPoints)
            UpdateBetweenIndicators(card, visibleCount);
        else if (_betweenIndicators.Count > 0)
            ClearBetweenIndicators();
    }

    private void EnsurePointIndicatorCount(int count, GameObject prefab, MultiPointCard card)
    {
        while (_pointIndicators.Count < count)
        {
            int pointIndex = _pointIndicators.Count;
            GameObject instance = Instantiate(prefab, transform);
            instance.name = $"CardPlacementIndicator_Point{pointIndex}";
            _pointIndicators.Add(instance);
            card.OnIndicatorSpawned(instance, pointIndex);
        }
    }

    private void UpdateBetweenIndicators(MultiPointCard card, int visibleCount)
    {
        int segmentCount = Mathf.Max(0, visibleCount - 1);

        if (string.IsNullOrEmpty(card.BetweenIndicatorPrefabName) || IndicatorPrefabRegistry.instance == null ||
            !IndicatorPrefabRegistry.instance.TryGet(card.BetweenIndicatorPrefabName, out GameObject prefab))
        {
            ClearBetweenIndicators();
            return;
        }

        while (_betweenIndicators.Count < segmentCount)
        {
            int segmentIndex = _betweenIndicators.Count;
            GameObject instance = Instantiate(prefab, transform);
            instance.name = $"CardPlacementIndicator_Between{segmentIndex}";
            _betweenIndicators.Add(instance);
            card.OnBetweenIndicatorSpawned(instance, segmentIndex);
        }

        for (int i = 0; i < _betweenIndicators.Count; i++)
        {
            bool active = i < segmentCount;
            _betweenIndicators[i].SetActive(active);
            if (!active) continue;

            Vector3 a = _pointIndicators[i].transform.position;
            Vector3 b = _pointIndicators[i + 1].transform.position;
            _betweenIndicators[i].transform.position = (a + b) * 0.5f;

            Vector3 flatDirection = new Vector3(b.x - a.x, 0f, b.z - a.z);
            if (flatDirection.sqrMagnitude > 0.0001f)
                _betweenIndicators[i].transform.rotation = Quaternion.LookRotation(flatDirection);
        }
    }

    private void ClearMultiPointIndicators()
    {
        foreach (GameObject go in _pointIndicators)
            if (go != null) Destroy(go);
        _pointIndicators.Clear();

        ClearBetweenIndicators();
        _multiPointCardEntityId = 0;
    }

    private void ClearBetweenIndicators()
    {
        foreach (GameObject go in _betweenIndicators)
            if (go != null) Destroy(go);
        _betweenIndicators.Clear();
    }

    private static Vector3 WorldPositionFor(float x, float y)
    {
        float height = WorldManager.instance.Handler.GetHeight((ushort)x, (ushort)y);
        return new Vector3(x, height, y);
    }
}
