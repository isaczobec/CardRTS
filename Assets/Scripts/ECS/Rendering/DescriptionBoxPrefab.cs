using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Thin wrapper for the description-box prefab (a background child that auto-sizes to its
// TMP_Text child's content — see the prefab's own Vertical Layout Group/Content Size
// Fitter setup). DescriptionTooltip owns positioning/showing/hiding this; SetText accepts
// TMPro rich-text markup directly (e.g. "<b>Title</b>\nDescription"), built by the caller.
//
// Callers must SetActive(true) on this GameObject BEFORE calling SetText — Unity's layout
// system can't correctly measure an inactive hierarchy, so text set while still inactive
// (e.g. right after a fresh Instantiate, or right after Hide()) would size the box wrong.
public class DescriptionBoxPrefab : MonoBehaviour
{
    [SerializeField] private TMP_Text _text;

    public RectTransform RectTransform => (RectTransform)transform;

    public void SetText(string text)
    {
        if (_text == null) return;

        _text.text = text;

        // TMP_Text regenerates its own geometry (which is what its ILayoutElement.
        // preferredHeight is computed from) lazily, on its own next update pass — without
        // forcing it here, the ForceRebuildLayoutImmediate right after can still see stale
        // geometry from before this text change, sizing the box for the PREVIOUS text
        // instead of this one (this was the "have to hover twice" bug: the fix only caught
        // up a frame late, by which point the box had already been shown at the wrong size).
        _text.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(RectTransform);
    }
}
