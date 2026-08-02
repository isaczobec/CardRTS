using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Upgrade shop — populates a grid with one UpgradeGameObject per UpgradeType registered in
/// UpgradeRegistry (mirrors ShopUIManager.PopulateGrid), shows a side-panel preview
/// (UpgradePreviewGameObject: larger image, description, price) while one is hovered, and,
/// on click (if affordable), opens a deck-picker window listing every card the local player
/// owns (CardGameObject instances, built the same way CardHandRenderer/ShopUIManager already
/// do). Clicking one of those cards sends a BuyUpgradeInput targeting it — BuyUpgradeSystem
/// deducts the gold and spawns the UpgradeComponent entity server-side (see
/// ECS/Upgrades/BuyUpgradeSystem.cs). Escape cancels the deck-picker without spending
/// anything.
///
/// Window open/close/toggle-key and Gold-affordability plumbing live on ShopWindowBase (see
/// ShopUIManager for the other shop sharing it).
/// </summary>
public class UpgradeShopUIManager : ShopWindowBase<UpgradeShopUIManager>
{
    [SerializeField] private UpgradeGameObject _upgradePrefab;
    [SerializeField] private Transform _gridContainer;

    [Header("Preview")]
    // Persistent, toggled on/off with hover — mirrors ShopUIManager._hoverPreviewCard.
    [SerializeField] private UpgradePreviewGameObject _previewPanel;

    [Header("Deck Picker")]
    // Shown once an affordable upgrade is clicked; lists every card the local player owns.
    [SerializeField] private GameObject _deckPickerWindow;
    [SerializeField] private CardGameObject _deckPickerCardPrefab;
    [SerializeField] private Transform _deckPickerContainer;

    [Header("Audio")]
    [SerializeField] private string _hoverSoundName = "ShopCardHover";
    [SerializeField] private string _buySoundName = "ShopBuy";

    // Category filter buttons — null (unassigned) means "no such filter button exists,"
    // handled the same as every other optional Inspector reference in this codebase.
    [Header("Filters")]
    [SerializeField] private Button _allFilterButton;
    [SerializeField] private Button _offenseFilterButton;
    [SerializeField] private Button _defenseFilterButton;
    [SerializeField] private Button _utilityFilterButton;

    private readonly List<UpgradeGameObject> _upgrades = new List<UpgradeGameObject>();
    private readonly Dictionary<UpgradeGameObject, CardUpgrade> _upgradeDefinitions = new Dictionary<UpgradeGameObject, CardUpgrade>();

    private readonly List<CardGameObject> _deckPickerCards = new List<CardGameObject>();
    // Set while _deckPickerWindow is open — which upgrade a deck-picker card click will
    // attach. Null means the picker is closed/there's nothing pending.
    private UpgradeType? _pendingUpgradeType;

    // Null = "All" (every category shown) — the default, so the grid opens unfiltered.
    private UpgradeCategory? _activeCategoryFilter;

    public void Initialize()
    {
        InitializeBase();

        PopulateGrid();
        RefreshAffordability();
        InitializeFilterButtons();

        if (_previewPanel != null)
            _previewPanel.gameObject.SetActive(false);
        if (_deckPickerWindow != null)
            _deckPickerWindow.SetActive(false);
    }

    // ── Filtering ────────────────────────────────────────────────────────────

    private void InitializeFilterButtons()
    {
        if (_allFilterButton != null)
            _allFilterButton.onClick.AddListener(() => SetCategoryFilter(null));
        if (_offenseFilterButton != null)
            _offenseFilterButton.onClick.AddListener(() => SetCategoryFilter(UpgradeCategory.Offense));
        if (_defenseFilterButton != null)
            _defenseFilterButton.onClick.AddListener(() => SetCategoryFilter(UpgradeCategory.Defense));
        if (_utilityFilterButton != null)
            _utilityFilterButton.onClick.AddListener(() => SetCategoryFilter(UpgradeCategory.Utility));
    }

    private void SetCategoryFilter(UpgradeCategory? category)
    {
        _activeCategoryFilter = category;
        ApplyFilters();
    }

    // Called by ShopWindowBase whenever the (optional) search bar's text changes.
    protected override void OnSearchTextChanged() => ApplyFilters();

    // Re-evaluates both the category filter and the search text together (an upgrade must
    // pass BOTH to show) — shared by SetCategoryFilter and OnSearchTextChanged since either
    // one changing requires re-checking every upgrade against the other's current state too.
    private void ApplyFilters()
    {
        string search = SearchText?.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);

        foreach (UpgradeGameObject go in _upgrades)
        {
            if (!_upgradeDefinitions.TryGetValue(go, out CardUpgrade upgrade)) continue;

            bool categoryMatches = _activeCategoryFilter == null || upgrade.Category == _activeCategoryFilter.Value;
            bool searchMatches = !hasSearch
                || upgrade.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || upgrade.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

            go.gameObject.SetActive(categoryMatches && searchMatches);
        }
    }

    protected override void Update()
    {
        base.Update();

        if (_pendingUpgradeType != null && Input.GetKeyDown(KeyCode.Escape))
            CloseDeckPicker();
    }

    // ── Upgrade grid ────────────────────────────────────────────────────────

    private void PopulateGrid()
    {
        if (_upgradePrefab == null || _gridContainer == null) return;

        foreach (UpgradeType type in System.Enum.GetValues(typeof(UpgradeType)))
        {
            if (!UpgradeRegistry.TryGet(type, out CardUpgrade upgrade)) continue;

            UpgradeGameObject go = Instantiate(_upgradePrefab, _gridContainer);
            go.name = $"ShopUpgrade_{type}";

            Sprite artwork = null;
            if (ImageRegistry.instance != null)
                ImageRegistry.instance.TryGet(upgrade.ImageName, out artwork);

            go.BuildUpgrade(artwork, upgrade.ShopGoldCost, upgrade.Title, upgrade.Description);

            go.HoverEntered += OnUpgradeHovered;
            go.HoverExited += OnUpgradeUnhovered;
            go.Clicked += OnUpgradeClicked;

            _upgrades.Add(go);
            _upgradeDefinitions[go] = upgrade;
        }
    }

    private void OnUpgradeHovered(UpgradeGameObject hovered)
    {
        if (!_upgradeDefinitions.TryGetValue(hovered, out CardUpgrade upgrade)) return;
        PlayShopSound(_hoverSoundName);

        if (_previewPanel == null) return;

        Sprite artwork = null;
        if (ImageRegistry.instance != null)
            ImageRegistry.instance.TryGet(upgrade.ImageName, out artwork);

        _previewPanel.BuildPreview(upgrade.Title, upgrade.Description, artwork, upgrade.ShopGoldCost);
        _previewPanel.gameObject.SetActive(true);
    }

    private void OnUpgradeUnhovered(UpgradeGameObject hovered)
    {
        if (_previewPanel == null) return;
        _previewPanel.gameObject.SetActive(false);
    }

    // Mirrors ShopUIManager.OnShopCardClicked's affordability guard — blocks opening the
    // deck picker for something BuyUpgradeSystem would just reject anyway.
    private void OnUpgradeClicked(UpgradeGameObject clicked)
    {
        if (!_upgradeDefinitions.TryGetValue(clicked, out CardUpgrade upgrade)) return;
        if (GetAvailableGold() < upgrade.ShopGoldCost) return;

        OpenDeckPicker(upgrade.Type);
    }

    protected override void RefreshAffordability()
    {
        int availableGold = GetAvailableGold();
        foreach (UpgradeGameObject go in _upgrades)
        {
            if (!_upgradeDefinitions.TryGetValue(go, out CardUpgrade upgrade)) continue;
            go.RefreshAffordability(availableGold, upgrade.ShopGoldCost);
        }
    }

    // ── Deck picker ─────────────────────────────────────────────────────────

    // "The player's deck" here means every card they own regardless of CardLocation (Deck or
    // Hand) — restricting to strictly CardLocation.Deck would make a card currently in hand
    // ineligible for an upgrade, which reads as an arbitrary gap rather than an intentional
    // rule.
    private void OpenDeckPicker(UpgradeType upgradeType)
    {
        _pendingUpgradeType = upgradeType;
        ClearDeckPicker();

        if (_deckPickerCardPrefab == null || _deckPickerContainer == null || Ecs == null) return;

        ComponentStore<CardComponent> cardStore = Ecs.GetComponentStore<CardComponent>();
        if (cardStore == null) return;

        ushort localPlayerId = LocalPlayerId();

        cardStore.ForEach((ulong id) =>
        {
            CardComponent card = cardStore.GetComponent(id);
            if (card.OwnerPlayerId != localPlayerId) return;
            if (!CardRegistry.TryGet(card.Type, out Card definition)) return;

            CardGameObject go = Instantiate(_deckPickerCardPrefab, _deckPickerContainer);
            go.name = $"DeckPickerCard_{id}";
            go.CardEntityId = id;

            Sprite artwork = null;
            if (ImageRegistry.instance != null)
                ImageRegistry.instance.TryGet(definition.ImageName, out artwork);

            go.BuildCard(definition.Title, definition.Description, artwork, definition.DefaultStats, definition.Cost,
                definition.Category, definition.BackgroundColor, definition.TextBackgroundColor, definition.EdgeColor);
            go.SetUpgradeIcons(UpgradeIconResolver.Resolve(Ecs, id));
            go.Clicked += OnDeckPickerCardClicked;

            _deckPickerCards.Add(go);
        });

        if (_deckPickerWindow != null)
            _deckPickerWindow.SetActive(true);
    }

    private void OnDeckPickerCardClicked(CardGameObject clicked)
    {
        if (_pendingUpgradeType == null) return;

        InputBuffer.EnqueueInput(new BuyUpgradeInput
        {
            UpgradeType         = _pendingUpgradeType.Value,
            TargetCardEntityId  = clicked.CardEntityId,
        });
        PlayShopSound(_buySoundName);

        CloseDeckPicker();
    }

    private void CloseDeckPicker()
    {
        _pendingUpgradeType = null;
        if (_deckPickerWindow != null)
            _deckPickerWindow.SetActive(false);
        ClearDeckPicker();
    }

    private void ClearDeckPicker()
    {
        foreach (CardGameObject go in _deckPickerCards)
            if (go != null) Destroy(go.gameObject);
        _deckPickerCards.Clear();
    }
}
