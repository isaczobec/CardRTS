using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Pans _content vertically with the mouse wheel while the cursor is over this component's
/// own GameObject — relies on Unity's EventSystem calling OnScroll automatically whenever
/// the pointer is over a raycast-target Graphic on this object, so no manual "is the mouse
/// over the window" polling is needed. Clamped so _content can never scroll past its own
/// bounds relative to _viewport in either direction.
///
/// Attach this to the VIEWPORT — the object with the Mask/Image that defines the visible
/// clipping window (e.g. CardWindow) — not to _content itself; moving the viewport along
/// with its own content wouldn't reveal anything new. _content (e.g. CardWindowContent)
/// should be a child of the viewport, top-anchored (AnchorMin 0.5,1 / AnchorMax 0.5,1 or
/// 0,1/1,1) so increasing anchoredPosition.y scrolls it up, revealing rows below.
/// </summary>
public class ScrollPanel : MonoBehaviour, IScrollHandler
{
    [SerializeField] private RectTransform _content;
    [SerializeField] private RectTransform _viewport;
    [SerializeField] private float _scrollSpeed = 40f;

    public void OnScroll(PointerEventData eventData)
    {
        if (_content == null) return;

        Vector2 pos = _content.anchoredPosition;
        pos.y = Mathf.Clamp(pos.y - eventData.scrollDelta.y * _scrollSpeed, 0f, MaxScrollY());
        _content.anchoredPosition = pos;
    }

    // Snaps back to the top of the list (anchoredPosition.y == 0 — see this class's own doc
    // comment on why increasing Y scrolls down through the content). Called whenever a
    // filter/search change re-shows/hides rows, so a result further down the (now
    // shorter/reordered) list doesn't stay scrolled past the visible window.
    public void ScrollToTop()
    {
        if (_content == null) return;

        Vector2 pos = _content.anchoredPosition;
        pos.y = 0f;
        _content.anchoredPosition = pos;
    }

    private float MaxScrollY()
    {
        if (_viewport == null) return 0f;
        return Mathf.Max(0f, _content.rect.height - _viewport.rect.height);
    }
}
