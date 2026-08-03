using TMPro;
using UnityEngine;

// Combined billboard + key-label wrapper for a single troop-marker badge (see
// TroopMarkerIconManager) — unlike AbilityStatusIconManager/ModifierIconManager, a marker is
// always exactly one fixed badge per bound key (no growing row of several icons per troop), so
// container and icon are combined into a single prefab/component here rather than split the
// way those two are. The marker graphic itself (if any) is just whatever's already on the
// prefab — nothing here needs to swap it dynamically, only the key letter changes per
// instance.
public class TroopMarkerPrefab : MonoBehaviour
{
    [SerializeField] private TMP_Text _keyLabel;

    public void SetKeyLabel(string text)
    {
        if (_keyLabel != null)
            _keyLabel.text = text;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;
    }
}
