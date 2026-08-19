using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Visual + input-forwarding component for a single card in hand. Purely "how do I look
/// and what happened to me" — CardHandRenderer owns all layout/selection/drag logic and
/// just subscribes to the events below; this never decides anything on its own.
/// For cards IN HAND, hover is deliberately NOT read off HoverEntered/Exited —
/// CardHandRenderer computes it against each card's static rest-slot position instead (see
/// CardHandRenderer.UpdateHoveredCard), since raycasting against the animated/raised
/// transform creates a feedback loop (raising a card moves its own hit-box out from under
/// the cursor, dropping hover, which lowers it again, regaining hover, ...). That problem is
/// specific to the hand's hover-raise animation though — a static grid (e.g. ShopUIManager's
/// shop cards, which don't move on hover) has no such feedback loop, so HoverEntered/Exited
/// are fine to use there.
/// </summary>
public class CardGameObject : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    // One icon+text row on the stats/cost panel. GameObjects/TMP_Text are wired in the
    // Inspector; the row's own Root is disabled when its value doesn't apply to a card
    // (StatsComponent.STAT_NA) or when its cost is 0.
    [Serializable]
    private class StatRow
    {
        public GameObject Root;
        public TMP_Text Text;
    }

    [Serializable]
    private class ResourceRow
    {
        public GameObject Root;
        public TMP_Text Text;

        [NonSerialized] public Color DefaultColor;
        [NonSerialized] public bool DefaultColorCaptured;
    }

    // The card's own artwork/illustration sprite.
    [SerializeField] private Image _image;
    // The Image on this GameObject's root carrying the card-frame material (the shader with
    // _BgColor/_TextBgColor/_EdgeColor/_TitleColor — see ApplyCardColors) — a different Image
    // than _image above, which only ever holds the artwork sprite.
    [SerializeField] private RawImage _cardBackgroundImage;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _text;
    // Shows Card.Category (Troop/Building/Spell) as text — set alongside the card's other
    // shader colors in BuildCard.
    [SerializeField] private TMP_Text _cardTypeText;

    private static readonly int BgColorId = Shader.PropertyToID("_BgColor");
    private static readonly int TextBgColorId = Shader.PropertyToID("_TextBgColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int TitleColorId = Shader.PropertyToID("_TitleColor");

    // Building = burgundy, Spell = purple, Troop = dark blue — explicit design ask. Derived
    // purely from Category, unlike BackgroundColor/TextBackgroundColor/EdgeColor, which are
    // themed per-card (see Card.cs).
    private static readonly Color BuildingTitleColor = new Color(0.5424528f, 0.0567257f, 0f);
    private static readonly Color SpellTitleColor = new Color(0.2792453f, 0f, 0.8254717f);
    private static readonly Color TroopTitleColor = new Color(0.08018869f, 0.1100438f, 1.0f);

    // Instantiated fresh per card in BuildCard (lazily, on first call) so each card's own
    // colors don't fight over one shared material's property values — mirrors
    // HealthBarPrefab's own per-instance _material pattern.
    private Material _cardMaterial;

    // Deactivated by default; CardHandRenderer activates one/both while this card is
    // hovered or selected (see SetPanelsVisible). Every row below is looked up through its
    // own Root, so a prefab variant that omits the stats/cost panel hierarchy entirely
    // (e.g. ShopUIManager's shop-card prefab) leaves these fields at their unassigned
    // default — BuildCard/RefreshAffordability/SetPanelsVisible all already null-check
    // before touching them, so that prefab variant works with no code changes needed here.
    [Header("Stats Panel")]
    [SerializeField] private GameObject _statsPanel;
    [SerializeField] private StatRow _maxHealthRow;
    [SerializeField] private StatRow _speedRow;
    [SerializeField] private StatRow _rangeRow;
    [SerializeField] private StatRow _armorRow;
    [SerializeField] private StatRow _damageRow;
    [SerializeField] private StatRow _attackSpeedRow;
    [SerializeField] private StatRow _spellResistRow;

    [Header("Resource Cost Panel")]
    [SerializeField] private GameObject _costPanel;
    [SerializeField] private ResourceRow _woodCostRow;
    [SerializeField] private ResourceRow _stoneCostRow;
    [SerializeField] private ResourceRow _metalCostRow;
    [SerializeField] private ResourceRow _gemsCostRow;
    [SerializeField] private ResourceRow _soulstonesCostRow;
    [SerializeField] private ResourceRow _goldCostRow;

    [SerializeField] private Color _insufficientResourceColor = Color.red;

    // Dims the whole card face; activated whenever the card's total cost isn't currently
    // affordable, regardless of hover/selection (unlike the stats/cost panels).
    [SerializeField] private GameObject _unaffordableOverlay;

    [Header("Shop")]
    // Optional — only wired on prefab variants that display ShopGoldCost as text (e.g.
    // ShopUIManager's hover-preview card). Null-checked same as every other field above.
    [SerializeField] private TMP_Text _shopGoldCostText;

    [Header("Upgrades")]
    // Optional — only wired on prefab variants that show which upgrades are equipped (e.g.
    // CardHandRenderer's hand cards, UpgradeShopUIManager's deck-picker cards). Null-checked
    // same as every other field above; a catalog card with no real entity behind it (e.g.
    // ShopUIManager's shop-grid/hover-preview cards) just never has SetUpgradeIcons called.
    [SerializeField] private Transform _upgradeIconContainer;
    [SerializeField] private UpgradeGameObject _upgradeIconPrefab;

    private readonly List<UpgradeGameObject> _upgradeIcons = new List<UpgradeGameObject>();

    private ResourceCost _cost;
    private int _shopGoldCost;

    // Set directly by CardHandRenderer right after instantiation — not part of BuildCard,
    // which is purely about how the card looks, not which ECS entity it represents.
    public ulong CardEntityId { get; set; }

    // Set directly by ShopUIManager right after instantiation, for shop-style prefabs that
    // call RefreshShopAffordability instead of RefreshAffordability (see both below). Also
    // pushes the value into _shopGoldCostText, if this prefab variant has one wired.
    public int ShopGoldCost
    {
        get => _shopGoldCost;
        set
        {
            _shopGoldCost = value;
            if (_shopGoldCostText != null)
                _shopGoldCostText.text = value.ToString();
        }
    }

    // Left/middle click only — see OnPointerClick. Right-click never selects a card; it's
    // routed to RightClicked instead so CardHandRenderer can implement its own
    // double-right-click-to-refund gesture.
    public event Action<CardGameObject> Clicked;
    public event Action<CardGameObject> RightClicked;
    public event Action<CardGameObject> DragStarted;
    public event Action<CardGameObject, PointerEventData> DragEnded;
    public event Action<CardGameObject> HoverEntered;
    public event Action<CardGameObject> HoverExited;

    public void BuildCard(string title, string description, Sprite image, StatsComponent stats, ResourceCost cost,
        CardCategory category, Color backgroundColor, Color textBackgroundColor, Color edgeColor)
    {
        if (_image != null)
            _image.sprite = image;

        if (_titleText != null)
            _titleText.text = title;

        if (_text != null)
            _text.text = description;

        if (_cardTypeText != null)
            _cardTypeText.text = category.ToString();

        ApplyCardColors(category, backgroundColor, textBackgroundColor, edgeColor);

        _cost = cost;

        SetStatRow(_maxHealthRow, stats.MaxHealth);
        SetStatRow(_speedRow, stats.Speed);
        SetStatRow(_rangeRow, stats.Range);
        SetStatRow(_armorRow, stats.Armor);
        SetStatRow(_damageRow, stats.Damage);
        SetStatRow(_attackSpeedRow, stats.AttackSpeed);
        SetStatRow(_spellResistRow, stats.SpellResist);

        SetResourceRowAmount(_woodCostRow, cost.Wood);
        SetResourceRowAmount(_stoneCostRow, cost.Stone);
        SetResourceRowAmount(_metalCostRow, cost.Metal);
        SetResourceRowAmount(_gemsCostRow, cost.Gems);
        SetResourceRowAmount(_soulstonesCostRow, cost.Soulstones);
        SetResourceRowAmount(_goldCostRow, cost.Gold);

        SetPanelsVisible(false);
    }

    // Lazily instantiates this card's own material (once — a second BuildCard call, if that
    // ever happens, reuses it rather than leaking another instance) and pushes the resolved
    // colors onto it. TitleColor is derived purely from category (see ResolveTitleColor),
    // never passed in directly — Building/Spell/Troop always get the same title color
    // regardless of which specific card it is.
    private void ApplyCardColors(CardCategory category, Color backgroundColor, Color textBackgroundColor, Color edgeColor)
    {
        if (_cardBackgroundImage == null) return;

        if (_cardMaterial == null)
        {
            _cardMaterial = Instantiate(_cardBackgroundImage.material);
            _cardBackgroundImage.material = _cardMaterial;
        }

        _cardMaterial.SetColor(BgColorId, backgroundColor);
        _cardMaterial.SetColor(TextBgColorId, textBackgroundColor);
        _cardMaterial.SetColor(EdgeColorId, edgeColor);
        _cardMaterial.SetColor(TitleColorId, ResolveTitleColor(category));
    }

    private static Color ResolveTitleColor(CardCategory category) => category switch
    {
        CardCategory.Building => BuildingTitleColor,
        CardCategory.Spell    => SpellTitleColor,
        _                     => TroopTitleColor,
    };

    // The instantiated material isn't a scene asset Unity tracks/destroys on its own —
    // without this it'd leak one Material object per card for the life of the process
    // (mirrors HealthBarPrefab's own OnDestroy).
    private void OnDestroy()
    {
        if (_cardMaterial != null) Destroy(_cardMaterial);
    }

    // Rebuilds the upgrade-icon row from a caller-resolved (icon, shopGoldCost) pair per
    // equipped upgrade — the caller (CardHandRenderer for hand cards, UpgradeShopUIManager
    // for deck-picker cards) is expected to have already walked
    // UpgradeQuery.ForEachUpgradeOnCard and looked each upgrade's ImageName up in
    // ImageRegistry itself, matching how artwork is always handed to BuildCard pre-resolved
    // rather than looked up in here. No-ops (leaves any existing icons as-is) if this
    // prefab variant has no container/prefab wired.
    public void SetUpgradeIcons(IReadOnlyList<(Sprite icon, int shopGoldCost, string title, string description)> upgrades)
    {
        if (_upgradeIconContainer == null || _upgradeIconPrefab == null) return;

        foreach (UpgradeGameObject icon in _upgradeIcons)
            if (icon != null) Destroy(icon.gameObject);
        _upgradeIcons.Clear();

        if (upgrades == null) return;

        foreach ((Sprite icon, int shopGoldCost, string title, string description) in upgrades)
        {
            UpgradeGameObject go = Instantiate(_upgradeIconPrefab, _upgradeIconContainer);
            go.BuildUpgrade(icon, shopGoldCost, title, description);
            _upgradeIcons.Add(go);
        }
    }

    // Called by CardHandRenderer whenever this card's hover/selection state changes
    // (every frame, whether or not it actually changed).
    public void SetPanelsVisible(bool visible)
    {
        SetPanelActive(_statsPanel, visible);
        SetPanelActive(_costPanel, visible);
    }

    // Reactivating a GameObject with a VerticalLayoutGroup/ContentSizeFitter after it's
    // been inactive doesn't reliably produce a correct layout on the very first frame —
    // Unity's automatic layout rebuild is deferred (CanvasUpdateRegistry) and can lag an
    // extra pass behind an active-state change, so the panel briefly renders at its stale
    // pre-hide size/position (observed as the stats panel rendering oversized, on top of
    // the resource panel, on the very first hover). Forcing an immediate rebuild right on
    // the inactive->active transition fixes this the first time a panel is shown, not just
    // from the second time on. Only done on an actual transition (activeSelf check), both
    // to avoid the cost of a full rebuild every frame and because ForceRebuildLayoutImmediate
    // requires the object to already be active.
    private static void SetPanelActive(GameObject panel, bool visible)
    {
        if (panel == null || panel.activeSelf == visible) return;

        panel.SetActive(visible);

        if (visible)
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
    }

    // Called by CardHandRenderer with the local player's current resources (on draw, and
    // whenever ResourcesChangedEvent fires) to color each cost row red if unaffordable.
    public void RefreshAffordability(PlayerResourcesComponent resources)
    {
        SetResourceRowAffordability(_woodCostRow, resources.WoodFloor >= _cost.Wood);
        SetResourceRowAffordability(_stoneCostRow, resources.StoneFloor >= _cost.Stone);
        SetResourceRowAffordability(_metalCostRow, resources.MetalFloor >= _cost.Metal);
        SetResourceRowAffordability(_gemsCostRow, resources.GemsFloor >= _cost.Gems);
        SetResourceRowAffordability(_soulstonesCostRow, resources.SoulstonesFloor >= _cost.Soulstones);
        SetResourceRowAffordability(_goldCostRow, resources.GoldFloor >= _cost.Gold);

        if (_unaffordableOverlay != null)
            _unaffordableOverlay.SetActive(!_cost.CanAfford(resources));
    }

    // Alternative to RefreshAffordability for shop-style prefabs (see ShopUIManager): there
    // are no per-resource cost rows to color and no ResourceCost to check against — just
    // the unaffordable overlay, compared against ShopGoldCost instead. blocked covers any
    // OTHER reason this specific purchase would be rejected server-side right now (the
    // match-wide purchase cap, or this card's own abilities not fitting on the ability bar —
    // see ShopPricingHelper.HasReachedPurchaseLimit/AbilityBarHelper.WouldExceedCapacity) —
    // folded into the same overlay rather than a separate one, since either way the card just
    // isn't buyable right now.
    public void RefreshShopAffordability(int availableGold, bool blocked = false)
    {
        if (_unaffordableOverlay != null)
            _unaffordableOverlay.SetActive(blocked || availableGold < ShopGoldCost);
    }

    private static void SetStatRow(StatRow row, int value)
    {
        if (row == null || row.Root == null) return;

        bool applicable = value != StatsComponent.STAT_NA;
        row.Root.SetActive(applicable);
        if (applicable && row.Text != null)
            row.Text.text = value.ToString();
    }

    private static void SetResourceRowAmount(ResourceRow row, int amount)
    {
        if (row == null || row.Root == null) return;

        bool hasCost = amount != 0;
        row.Root.SetActive(hasCost);
        if (hasCost && row.Text != null)
            row.Text.text = amount.ToString();
    }

    private void SetResourceRowAffordability(ResourceRow row, bool canAfford)
    {
        if (row == null || row.Text == null) return;

        if (!row.DefaultColorCaptured)
        {
            row.DefaultColor = row.Text.color;
            row.DefaultColorCaptured = true;
        }

        row.Text.color = canAfford ? row.DefaultColor : _insufficientResourceColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
            RightClicked?.Invoke(this);
        else
            Clicked?.Invoke(this);
    }

    public void OnBeginDrag(PointerEventData eventData) => DragStarted?.Invoke(this);
    // Per-frame drag position is driven by CardHandRenderer reading Input.mousePosition
    // directly (needs the same lift-threshold logic every frame regardless of whether the
    // pointer actually moved), so OnDrag itself doesn't need to do anything.
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) => DragEnded?.Invoke(this, eventData);
    public void OnPointerEnter(PointerEventData eventData) => HoverEntered?.Invoke(this);
    public void OnPointerExit(PointerEventData eventData) => HoverExited?.Invoke(this);
}
