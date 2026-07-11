using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Visual + input-forwarding component for a single card in hand. Purely "how do I look
/// and what happened to me" — CardHandRenderer owns all layout/selection/drag logic and
/// just subscribes to the events below; this never decides anything on its own.
/// Hover is deliberately NOT handled via IPointerEnterHandler/Exit here — CardHandRenderer
/// computes it against each card's static rest-slot position instead (see
/// CardHandRenderer.UpdateHoveredCard), since raycasting against the animated/raised
/// transform creates a feedback loop (raising a card moves its own hit-box out from under
/// the cursor, dropping hover, which lowers it again, regaining hover, ...).
/// </summary>
public class CardGameObject : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
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

    [SerializeField] private Image _image;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _text;

    // Deactivated by default; CardHandRenderer activates one/both while this card is
    // hovered or selected (see SetPanelsVisible).
    [Header("Stats Panel")]
    [SerializeField] private GameObject _statsPanel;
    [SerializeField] private StatRow _maxHealthRow;
    [SerializeField] private StatRow _speedRow;
    [SerializeField] private StatRow _rangeRow;
    [SerializeField] private StatRow _armorRow;
    [SerializeField] private StatRow _damageRow;
    [SerializeField] private StatRow _attackSpeedRow;

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

    private ResourceCost _cost;

    // Set directly by CardHandRenderer right after instantiation — not part of BuildCard,
    // which is purely about how the card looks, not which ECS entity it represents.
    public ulong CardEntityId { get; set; }

    public event Action<CardGameObject> Clicked;
    public event Action<CardGameObject> DragStarted;
    public event Action<CardGameObject, PointerEventData> DragEnded;

    public void BuildCard(string title, string description, Sprite image, StatsComponent stats, ResourceCost cost)
    {
        if (_image != null)
            _image.sprite = image;

        if (_titleText != null)
            _titleText.text = title;

        if (_text != null)
            _text.text = description;

        _cost = cost;

        SetStatRow(_maxHealthRow, stats.MaxHealth);
        SetStatRow(_speedRow, stats.Speed);
        SetStatRow(_rangeRow, stats.Range);
        SetStatRow(_armorRow, stats.Armor);
        SetStatRow(_damageRow, stats.Damage);
        SetStatRow(_attackSpeedRow, stats.AttackSpeed);

        SetResourceRowAmount(_woodCostRow, cost.Wood);
        SetResourceRowAmount(_stoneCostRow, cost.Stone);
        SetResourceRowAmount(_metalCostRow, cost.Metal);
        SetResourceRowAmount(_gemsCostRow, cost.Gems);
        SetResourceRowAmount(_soulstonesCostRow, cost.Soulstones);
        SetResourceRowAmount(_goldCostRow, cost.Gold);

        SetPanelsVisible(false);
    }

    // Called by CardHandRenderer whenever this card's hover/selection state changes.
    public void SetPanelsVisible(bool visible)
    {
        if (_statsPanel != null) _statsPanel.SetActive(visible);
        if (_costPanel != null) _costPanel.SetActive(visible);
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

    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(this);
    public void OnBeginDrag(PointerEventData eventData) => DragStarted?.Invoke(this);
    // Per-frame drag position is driven by CardHandRenderer reading Input.mousePosition
    // directly (needs the same lift-threshold logic every frame regardless of whether the
    // pointer actually moved), so OnDrag itself doesn't need to do anything.
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) => DragEnded?.Invoke(this, eventData);
}
