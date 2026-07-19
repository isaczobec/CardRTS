using UnityEngine;

// Reusable cursor-following tooltip for hover descriptions (ability icons — see
// AbilityBarUI — and upgrade icons — see UpgradeGameObject's ShowDescriptionOnHover flag).
// Lazily instantiates _prefab once on first use and reuses that same instance afterward
// (toggled active/inactive), rather than spawning/destroying a fresh one per hover.
//
// While shown, follows the cursor every frame, anchoring whichever corner of the box is
// nearest the cursor's own screen quadrant TO the cursor — so the box always expands toward
// screen center, away from whichever edge the cursor is closest to, instead of getting
// clipped off-screen.
public class DescriptionTooltip : Singleton<DescriptionTooltip>
{
    [SerializeField] private DescriptionBoxPrefab _prefab;
    // Parent to instantiate into — must be under the same Canvas the hovered icons live on.
    [SerializeField] private RectTransform _container;

    private DescriptionBoxPrefab _instance;

    public void Show(string title, string description)
    {
        EnsureInstance();
        if (_instance == null) return;

        // Must activate BEFORE SetText — Unity's layout system can't correctly measure an
        // inactive hierarchy, so setting text first (the previous order here) sized the box
        // off whatever it last was, not the new content (see DescriptionBoxPrefab.SetText's
        // own doc comment for the other half of this fix).
        _instance.gameObject.SetActive(true);
        _instance.SetText($"<b>{title}</b>\n{description}");

        FollowCursor(_instance.RectTransform);
    }

    public void Hide()
    {
        if (_instance != null)
            _instance.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_instance == null || !_instance.gameObject.activeSelf) return;
        FollowCursor(_instance.RectTransform);
    }

    private void EnsureInstance()
    {
        if (_instance != null) return;
        if (_prefab == null || _container == null) return;

        _instance = Instantiate(_prefab, _container);
        _instance.name = "DescriptionBox";
    }

    // Mirrors SelectionManager.UpdateDragBox's canvas-space conversion (CanvasScaler-safe —
    // raw Input.mousePosition can't be written straight into anchoredPosition), but picks
    // the box's anchor corner from which screen quadrant the cursor is in.
    private void FollowCursor(RectTransform box)
    {
        RectTransform parent = box.parent as RectTransform;
        if (parent == null) return;

        Canvas canvas = box.GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        Vector2 screenPoint = Input.mousePosition;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, cam, out Vector2 localPoint))
            return;

        // Left half of the screen -> anchor the box's left edge to the cursor (box expands
        // right); right half -> anchor its right edge (box expands left). Same idea
        // vertically. This keeps the box on-screen regardless of which corner the cursor is
        // hovering near.
        float pivotX = screenPoint.x < Screen.width * 0.5f ? 0f : 1f;
        float pivotY = screenPoint.y < Screen.height * 0.5f ? 0f : 1f;
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(pivotX, pivotY);

        Vector2 anchorPointLocal = new Vector2(
            Mathf.Lerp(parent.rect.xMin, parent.rect.xMax, pivotX),
            Mathf.Lerp(parent.rect.yMin, parent.rect.yMax, pivotY));

        box.anchoredPosition = localPoint - anchorPointLocal;
    }
}
