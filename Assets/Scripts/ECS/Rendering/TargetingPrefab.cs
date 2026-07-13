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
        if (_scalableTransform != null)
            _scalableTransform.localScale = Vector3.one * scale;
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
    }
}
