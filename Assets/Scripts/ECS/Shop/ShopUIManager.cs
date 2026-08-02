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
/// here unchanged. The unaffordable overlay is instead driven by RefreshAffordability,
/// compared against the local player's live PlayerResourcesComponent.GoldFloor — there's no
/// separate shop currency in this project, so match Gold doubles as the shop's currency too.
///
/// Window open/close/toggle-key and Gold-affordability plumbing live on ShopWindowBase (see
/// UpgradeShopUIManager for the other shop sharing it) — this class only owns populating the
/// grid and the hover-preview card.
/// </summary>
public class ShopUIManager : ShopWindowBase<ShopUIManager>
{
    [SerializeField] private CardGameObject _shopCardPrefab;
    [SerializeField] private Transform _gridContainer;
    // Optional — assign the grid's ScrollPanel (see that class) so a filter/search change
    // scrolls back to the top instead of leaving the view wherever it happened to be.
    [SerializeField] private ScrollPanel _scrollPanel;

    [Header("Hover Preview")]
    // A separate, persistent CardGameObject (full stats/cost panels wired, unlike
    // _shopCardPrefab) that's rebuilt from whichever shop card is currently hovered and
    // toggled on/off with it. Starts hidden; PopulateGrid leaves it that way until the
    // first hover.
    [SerializeField] private CardGameObject _hoverPreviewCard;

    [Header("Audio")]
    [SerializeField] private string _hoverSoundName = "ShopCardHover";
    [SerializeField] private string _buySoundName = "ShopBuy";

    // Category filter buttons — null (unassigned) means "no such filter button exists,"
    // handled the same as every other optional Inspector reference in this codebase.
    [Header("Filters")]
    [SerializeField] private Button _allFilterButton;
    [SerializeField] private Button _troopFilterButton;
    [SerializeField] private Button _buildingFilterButton;
    [SerializeField] private Button _spellFilterButton;

    private readonly List<CardGameObject> _shopCards = new List<CardGameObject>();
    private readonly Dictionary<CardGameObject, Card> _shopCardDefinitions = new Dictionary<CardGameObject, Card>();

    // Null = "All" (every category shown) — the default, so the grid opens unfiltered.
    private CardCategory? _activeCategoryFilter;

    public void Initialize()
    {
        InitializeBase();

        PopulateGrid();
        RefreshAffordability();
        InitializeFilterButtons();

        if (_hoverPreviewCard != null)
            _hoverPreviewCard.gameObject.SetActive(false);
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    private void InitializeFilterButtons()
    {
        if (_allFilterButton != null)
            _allFilterButton.onClick.AddListener(() => SetCategoryFilter(null));
        if (_troopFilterButton != null)
            _troopFilterButton.onClick.AddListener(() => SetCategoryFilter(CardCategory.Troop));
        if (_buildingFilterButton != null)
            _buildingFilterButton.onClick.AddListener(() => SetCategoryFilter(CardCategory.Building));
        if (_spellFilterButton != null)
            _spellFilterButton.onClick.AddListener(() => SetCategoryFilter(CardCategory.Spell));
    }

    private void SetCategoryFilter(CardCategory? category)
    {
        _activeCategoryFilter = category;
        ApplyFilters();
    }

    // Called by ShopWindowBase whenever the (optional) search bar's text changes.
    protected override void OnSearchTextChanged() => ApplyFilters();

    // Re-evaluates both the category filter and the search text together (a card must pass
    // BOTH to show) — shared by SetCategoryFilter and OnSearchTextChanged since either one
    // changing requires re-checking every card against the other's current state too.
    private void ApplyFilters()
    {
        string search = SearchText?.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);

        foreach (CardGameObject go in _shopCards)
        {
            if (!_shopCardDefinitions.TryGetValue(go, out Card card)) continue;

            bool categoryMatches = _activeCategoryFilter == null || card.Category == _activeCategoryFilter.Value;
            bool searchMatches = !hasSearch
                || card.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || card.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

            go.gameObject.SetActive(categoryMatches && searchMatches);
        }

        // A GridLayoutGroup on _gridContainer only reflows around the newly hidden/shown
        // cards on Unity's own next Canvas update pass, not synchronously here — without
        // forcing it immediately, ScrollToTop below computes/applies against the OLD
        // (pre-filter) layout, which is why the grid previously looked wrong (gaps, cards
        // still in stale positions) until something else (a manual scroll) forced a rebuild.
        if (_gridContainer is RectTransform gridRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridRect);

        _scrollPanel?.ScrollToTop();
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

            go.BuildCard(card.Title, card.Description, artwork, card.DefaultStats, card.Cost,
                card.Category, card.BackgroundColor, card.TextBackgroundColor, card.EdgeColor);
            go.ShopGoldCost = ShopPricingHelper.GetEffectiveShopGoldCost(Ecs, LocalPlayerId(), card);

            go.HoverEntered += OnShopCardHovered;
            go.HoverExited += OnShopCardUnhovered;
            go.Clicked += OnShopCardClicked;

            _shopCards.Add(go);
            _shopCardDefinitions[go] = card;
        }
    }

    // Mirrors CardHandRenderer.SelectCard's own affordability guard — blocks sending a
    // BuyCardInput that BuyCardSystem would just reject anyway, same as clicking an
    // unaffordable hand card never selects it.
    private void OnShopCardClicked(CardGameObject clickedCard)
    {
        if (!_shopCardDefinitions.TryGetValue(clickedCard, out Card card)) return;
        if (GetAvailableGold() < ShopPricingHelper.GetEffectiveShopGoldCost(Ecs, LocalPlayerId(), card)) return;

        InputBuffer.EnqueueInput(new BuyCardInput { CardType = card.Type });
        PlayShopSound(_buySoundName);
    }

    private void OnShopCardHovered(CardGameObject hoveredCard)
    {
        if (!_shopCardDefinitions.TryGetValue(hoveredCard, out Card card)) return;
        PlayShopSound(_hoverSoundName);

        if (_hoverPreviewCard == null) return;

        Sprite artwork = null;
        if (ImageRegistry.instance != null)
            ImageRegistry.instance.TryGet(card.ImageName, out artwork);

        _hoverPreviewCard.BuildCard(card.Title, card.Description, artwork, card.DefaultStats, card.Cost,
            card.Category, card.BackgroundColor, card.TextBackgroundColor, card.EdgeColor);
        _hoverPreviewCard.ShopGoldCost = ShopPricingHelper.GetEffectiveShopGoldCost(Ecs, LocalPlayerId(), card);
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

    // Also re-prices every card, not just re-dims them — a purchase can cross the
    // ShopPricingHelper discount threshold, which changes what every remaining card costs.
    protected override void RefreshAffordability()
    {
        int availableGold = GetAvailableGold();
        ushort localPlayerId = LocalPlayerId();

        foreach (CardGameObject go in _shopCards)
        {
            if (!_shopCardDefinitions.TryGetValue(go, out Card card)) continue;

            go.ShopGoldCost = ShopPricingHelper.GetEffectiveShopGoldCost(Ecs, localPlayerId, card);
            go.RefreshShopAffordability(availableGold);
        }
    }
}
