using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Visual + input-forwarding component for a single upgrade icon — used both for the shop
// grid (see UpgradeShopUIManager, where _priceText is wired) and, without a click listener
// attached, as the small badge CardGameObject spawns per equipped upgrade (see
// CardGameObject.SetUpgradeIcons, where a badge prefab variant can leave _priceText/
// _unaffordableOverlay unwired since neither applies there).
public class UpgradeGameObject : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image _image;

    // Optional — only wired on prefab variants that display the shop price as text (e.g.
    // UpgradeShopUIManager's grid icon; a CardGameObject upgrade badge likely omits it).
    [SerializeField] private TMP_Text _priceText;

    // Optional — dims the icon while the local player can't currently afford this upgrade.
    [SerializeField] private GameObject _unaffordableOverlay;

    // If true, hovering this icon shows a DescriptionTooltip with the upgrade's name and
    // description. Leave false on a prefab variant that already shows this some other way
    // (e.g. the shop grid, which has its own larger side-panel preview — see
    // UpgradeShopUIManager) — set true where there'd otherwise be no way to see what the
    // upgrade does, e.g. CardGameObject's small badge icons.
    [SerializeField] private bool _showDescriptionOnHover;

    private string _title;
    private string _description;

    public event Action<UpgradeGameObject> Clicked;
    public event Action<UpgradeGameObject> HoverEntered;
    public event Action<UpgradeGameObject> HoverExited;

    public void BuildUpgrade(Sprite image, int shopGoldCost, string title = null, string description = null)
    {
        if (_image != null)
            _image.sprite = image;

        if (_priceText != null)
            _priceText.text = shopGoldCost.ToString();

        _title = title;
        _description = description;
    }

    public void RefreshAffordability(int availableGold, int shopGoldCost)
    {
        if (_unaffordableOverlay != null)
            _unaffordableOverlay.SetActive(availableGold < shopGoldCost);
    }

    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(this);

    public void OnPointerEnter(PointerEventData eventData)
    {
        HoverEntered?.Invoke(this);
        if (_showDescriptionOnHover)
            DescriptionTooltip.instance?.Show(_title, _description);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HoverExited?.Invoke(this);
        if (_showDescriptionOnHover)
            DescriptionTooltip.instance?.Hide();
    }
}
