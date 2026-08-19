using UnityEngine;
using UnityEngine.UI;

// Flat world-space ring shown on a troop while it is targeted by at least one friendly
// troop, rendered as a plain SpriteRenderer lying on the ground plane (see
// SelectionPrefab). Managed the same way as SelectionPrefab (instantiated per troop,
// repositioned every frame to follow it), but stays hidden until SetTargeted is called.
// Colors itself based on whether the target was player-assigned or picked automatically by AI.
public class TargetingPrefab : MonoBehaviour
{
    [SerializeField] private Image _image;
    [SerializeField] private Color _playerAssignedColor = Color.red;
    [SerializeField] private Color _automaticColor = Color.yellow;

    // Transform to scale via SetScale — separate from this GameObject's own root transform
    // since SelectionManager drives the root's position every frame (see
    // SelectionManager.ApplyTargetingPosition), and a uniform scale set here should
    // persist independently of that.
    [SerializeField] private Transform _scalableTransform;

    // Extra multiplier (on top of the base scale from SetScale) applied while at least one
    // of the friendly troops currently targeting this entity is itself selected — explicit
    // design ask ("the targeting prefabs around the entities [selected troops are]
    // targetting are made 1.4x larger... reverts when unselected"). See SetTargeterSelected,
    // driven every frame by SelectionManager.RefreshTargetingVisuals.
    private const float SelectedTargeterScaleMultiplier = 1.4f;

    // Base scale from SetScale (SelectableComponent.Scale) — kept so SetTargeterSelected can
    // multiply on top of it rather than overwrite it outright.
    private float _baseScale = 1f;
    private bool _targeterSelected;

    public bool IsTargeted { get; private set; }
    public TargetKind? CurrentKind { get; private set; }

    void Awake()
    {
        if (_image == null)
            _image = GetComponentInChildren<Image>();

        gameObject.SetActive(false);
    }

    // Called once by SelectionManager right after instantiation (see SetupSelection), using
    // SelectableComponent.Scale.
    public void SetScale(float scale)
    {
        _baseScale = scale;
        ApplyScale();
    }

    // Called every frame by RefreshTargetingVisuals for every currently-targeted entity —
    // no-ops when nothing actually changed, so redundant calls (the overwhelmingly common
    // case, since which friendly troop is selected rarely changes tick-to-tick) are cheap.
    public void SetTargeterSelected(bool selected)
    {
        if (_targeterSelected == selected) return;
        _targeterSelected = selected;
        ApplyScale();
    }

    private void ApplyScale()
    {
        if (_scalableTransform == null) return;
        float multiplier = _targeterSelected ? SelectedTargeterScaleMultiplier : 1f;
        _scalableTransform.localScale = Vector3.one * (_baseScale * multiplier);
    }

    public void SetTargeted(TargetKind kind)
    {
        IsTargeted = true;
        CurrentKind = kind;
        _image.color = kind == TargetKind.Automatic ? _automaticColor : _playerAssignedColor;
        gameObject.SetActive(true);
    }

    public void SetUntargeted()
    {
        IsTargeted = false;
        CurrentKind = null;
        gameObject.SetActive(false);

        // Reverts the highlight so a later re-target starts fresh rather than potentially
        // reading as already-highlighted for a frame before RefreshTargetingVisuals catches
        // up — see SelectedTargeterScaleMultiplier's own doc comment.
        if (_targeterSelected)
        {
            _targeterSelected = false;
            ApplyScale();
        }
    }
}
