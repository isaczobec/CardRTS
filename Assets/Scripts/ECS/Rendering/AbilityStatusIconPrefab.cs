using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Thin wrapper for a single per-troop ability status icon — AbilityStatusIconManager owns
// which icon/cooldown/charges to show and when; this just applies it. Mirrors
// ModifierIconPrefab's own thin-wrapper role, but with AbilityBarUI's fuller per-slot layout
// (icon + cooldown text/overlay + charges text) instead of ModifierIconPrefab's icon+duration
// only — a cast cooldown needs the radial wipe/charge count to read at a glance the way a
// plain countdown number for a modifier's remaining duration doesn't.
public class AbilityStatusIconPrefab : MonoBehaviour
{
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _cooldownText;
    // Filled-type Image (radial/wipe) drawn over the icon while on cooldown — fillAmount
    // goes from 1 (just used) down to 0 (ready) over the course of the cooldown, mirroring
    // AbilityBarUI's own CooldownOverlay.
    [SerializeField] private Image _cooldownOverlay;
    // Shown only for a MaxCharges > 1 ability (see SetCharges) — mirrors AbilityBarUI's own
    // ChargesText.
    [SerializeField] private TMP_Text _chargesText;

    public void SetIcon(Sprite sprite)
    {
        if (_icon != null)
            _icon.sprite = sprite;
    }

    // Pass null/empty cooldownSecondsText to hide the cooldown text (ability's off cooldown).
    // fillAmount is always applied to the overlay regardless (0 once ready).
    public void SetCooldown(string cooldownSecondsText, float fillAmount)
    {
        if (_cooldownText != null)
        {
            bool show = !string.IsNullOrEmpty(cooldownSecondsText);
            _cooldownText.gameObject.SetActive(show);
            if (show)
                _cooldownText.text = cooldownSecondsText;
        }

        if (_cooldownOverlay != null)
            _cooldownOverlay.fillAmount = fillAmount;
    }

    // Pass null/empty chargesText to hide the charges label entirely (a MaxCharges <= 1
    // ability has nothing meaningful to show here).
    public void SetCharges(string chargesText)
    {
        if (_chargesText == null) return;

        bool show = !string.IsNullOrEmpty(chargesText);
        _chargesText.gameObject.SetActive(show);
        if (show)
            _chargesText.text = chargesText;
    }
}
