using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Side-panel preview shown while an upgrade in the shop grid is hovered — a larger image, a
// description box under it, and a price indicator. Plays the same role as ShopUIManager's
// _hoverPreviewCard (a CardGameObject), but as its own simple type since an upgrade has no
// stats/resource-cost panel to reuse CardGameObject for.
public class UpgradePreviewGameObject : MonoBehaviour
{
    [SerializeField] private Image _image;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _descriptionText;
    [SerializeField] private TMP_Text _priceText;

    public void BuildPreview(string title, string description, Sprite image, int shopGoldCost)
    {
        if (_image != null)
            _image.sprite = image;

        if (_titleText != null)
            _titleText.text = title;

        if (_descriptionText != null)
            _descriptionText.text = description;

        if (_priceText != null)
            _priceText.text = shopGoldCost.ToString();
    }
}
