using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Thin wrapper for a single modifier icon — ModifierIconManager owns which icon/duration to
// show and when; this just applies it. _durationText is expected to render on top of
// _image (the prefab's own hierarchy/layout decides that, same as AbilityBarUI's
// CooldownText-over-Icon layout) and is hidden entirely for an infinite-duration modifier.
public class ModifierIconPrefab : MonoBehaviour
{
    [SerializeField] private Image _image;
    [SerializeField] private TMP_Text _durationText;

    public void SetIcon(Sprite icon)
    {
        if (_image != null)
            _image.sprite = icon;
    }

    // Pass null/empty to hide the duration text entirely (an infinite modifier).
    public void SetDuration(string text)
    {
        if (_durationText == null) return;

        bool show = !string.IsNullOrEmpty(text);
        _durationText.gameObject.SetActive(show);
        if (show)
            _durationText.text = text;
    }
}
