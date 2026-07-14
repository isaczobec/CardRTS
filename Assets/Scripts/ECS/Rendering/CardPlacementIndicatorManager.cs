using UnityEngine;

// Shows a prefab that follows the player's mouse (snapped to the ground) while a
// SpawnAtPointCard with a non-empty IndicatorPrefabName is selected or being dragged (see
// CardHandRenderer.ResolveActiveCard), previewing where the card's entity will land.
// Hidden whenever no such card is active, or the ground raycast fails (e.g. cursor off
// the horizon).
//
// Separate from the ECS architecture, like SelectionManager/CardRangeIndicatorManager —
// purely reactive to CardHandRenderer's selection state and the live cursor position, no
// ECS event subscriptions needed.
public class CardPlacementIndicatorManager : Singleton<CardPlacementIndicatorManager>
{
    private string _currentIndicatorName;
    private GameObject _currentIndicator;

    public void Initialize() { }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        SpawnAtPointCard activeCard = ResolveActiveSpawnCard();
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
            float height = WorldManager.instance.Handler.GetHeight((ushort)x, (ushort)y);
            _currentIndicator.transform.position = new Vector3(x, height, y);
        }
        else
        {
            SetIndicatorActive(false);
        }
    }

    private SpawnAtPointCard ResolveActiveSpawnCard()
    {
        if (CardHandRenderer.instance == null) return null;
        return CardHandRenderer.instance.ResolveActiveCard() as SpawnAtPointCard;
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
}
