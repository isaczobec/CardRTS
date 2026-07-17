using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Populates the card shop grid with one CardGameObject instance per CardType registered in
/// CardRegistry, built once at match start. Unlike CardHandRenderer's hand cards, shop cards
/// aren't tied to a specific card ENTITY (there's no CardComponent behind them) — they're a
/// static catalog built straight off each Card's own metadata (Title, Description,
/// DefaultStats, ImageName, ShopGoldCost). _shopCardPrefab is expected to be a
/// CardGameObject variant with its stats/cost panels omitted — BuildCard already tolerates
/// that gracefully (see CardGameObject), so the exact same call CardHandRenderer uses works
/// here unchanged. The unaffordable overlay is instead driven by RefreshShopAffordability,
/// compared against the local player's live PlayerResourcesComponent.GoldFloor — there's no
/// separate shop currency in this project, so match Gold doubles as the shop's currency too.
///
/// Also owns opening/closing _shopWindow (P key or _closeButton) and populating
/// _hoverPreviewCard — a single shared, full-detail CardGameObject (stats/cost panels
/// included, plus its own ShopGoldCost text) shown while any shop-grid card is hovered.
/// </summary>
public class ShopUIManager : Singleton<ShopUIManager>
{
    [SerializeField] private CardGameObject _shopCardPrefab;
    [SerializeField] private Transform _gridContainer;

    [Header("Window")]
    [SerializeField] private GameObject _shopWindow;
    [SerializeField] private Button _closeButton;

    [Header("Hover Preview")]
    // A separate, persistent CardGameObject (full stats/cost panels wired, unlike
    // _shopCardPrefab) that's rebuilt from whichever shop card is currently hovered and
    // toggled on/off with it. Starts hidden; PopulateGrid leaves it that way until the
    // first hover.
    [SerializeField] private CardGameObject _hoverPreviewCard;

    private ECS _ecs;
    private ulong _localPlayerResourceEntityId;

    private readonly List<CardGameObject> _shopCards = new List<CardGameObject>();
    private readonly Dictionary<CardGameObject, Card> _shopCardDefinitions = new Dictionary<CardGameObject, Card>();

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;

        PopulateGrid();
        RefreshAffordability();

        if (_hoverPreviewCard != null)
            _hoverPreviewCard.gameObject.SetActive(false);

        if (_closeButton != null)
            _closeButton.onClick.AddListener(CloseShop);

        TickManager.instance.ServerFlagEvents.Subscribe<ResourcesChangedEvent>(OnResourcesChanged);
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        if (Input.GetKeyDown(KeyCode.P))
            ToggleShop();
    }

    private void ToggleShop()
    {
        if (_shopWindow == null) return;
        _shopWindow.SetActive(!_shopWindow.activeSelf);
    }

    private void CloseShop()
    {
        if (_shopWindow == null) return;
        _shopWindow.SetActive(false);
    }

    private void PopulateGrid()
    {
        if (_shopCardPrefab == null || _gridContainer == null) return;

        foreach (CardType type in Enum.GetValues(typeof(CardType)))
        {
            if (!CardRegistry.TryGet(type, out Card card)) continue;

            CardGameObject go = Instantiate(_shopCardPrefab, _gridContainer);
            go.name = $"ShopCard_{type}";

            Sprite artwork = null;
            if (ImageRegistry.instance != null)
                ImageRegistry.instance.TryGet(card.ImageName, out artwork);

            go.BuildCard(card.Title, card.Description, artwork, card.DefaultStats, card.Cost);
            go.ShopGoldCost = card.ShopGoldCost;

            go.HoverEntered += OnShopCardHovered;
            go.HoverExited += OnShopCardUnhovered;

            _shopCards.Add(go);
            _shopCardDefinitions[go] = card;
        }
    }

    private void OnShopCardHovered(CardGameObject hoveredCard)
    {
        if (_hoverPreviewCard == null) return;
        if (!_shopCardDefinitions.TryGetValue(hoveredCard, out Card card)) return;

        Sprite artwork = null;
        if (ImageRegistry.instance != null)
            ImageRegistry.instance.TryGet(card.ImageName, out artwork);

        _hoverPreviewCard.BuildCard(card.Title, card.Description, artwork, card.DefaultStats, card.Cost);
        _hoverPreviewCard.ShopGoldCost = card.ShopGoldCost;
        // BuildCard hides the stats/cost panels by default (the behavior CardHandRenderer
        // wants for cards in hand) — the preview card should always show them while active.
        _hoverPreviewCard.SetPanelsVisible(true);
        _hoverPreviewCard.gameObject.SetActive(true);
    }

    private void OnShopCardUnhovered(CardGameObject hoveredCard)
    {
        if (_hoverPreviewCard == null) return;
        _hoverPreviewCard.gameObject.SetActive(false);
    }

    private ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    // Re-resolved lazily (mirroring CardHandRenderer.ResolveLocalPlayerResourceEntity)
    // rather than cached forever, in case the local player's entity doesn't exist yet the
    // first time this is queried.
    private ulong ResolveLocalPlayerResourceEntity()
    {
        if (_ecs == null) return 0;

        ComponentStore<PlayerResourcesComponent> resourceStore = _ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore != null && _localPlayerResourceEntityId != 0 && resourceStore.HasComponent(_localPlayerResourceEntityId))
            return _localPlayerResourceEntityId;

        _localPlayerResourceEntityId = ResourceHelper.FindPlayerResourcesEntity(_ecs, LocalPlayerId());
        return _localPlayerResourceEntityId;
    }

    private void OnResourcesChanged(ResourcesChangedEvent e)
    {
        if (e.EntityId != ResolveLocalPlayerResourceEntity()) return;
        RefreshAffordability();
    }

    private void RefreshAffordability()
    {
        ulong resourceEntityId = ResolveLocalPlayerResourceEntity();
        if (resourceEntityId == 0) return;

        ComponentStore<PlayerResourcesComponent> resourceStore = _ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || !resourceStore.HasComponent(resourceEntityId)) return;

        int availableGold = resourceStore.GetComponent(resourceEntityId).GoldFloor;
        foreach (CardGameObject go in _shopCards)
            go.RefreshShopAffordability(availableGold);
    }
}
