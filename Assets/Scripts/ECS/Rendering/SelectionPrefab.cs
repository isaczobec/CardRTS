using UnityEngine;
using UnityEngine.UI;

// Flat world-space selection ring, rendered as a plain SpriteRenderer lying on the ground
// plane. The world is completely flat, so there's no need to project a decal onto uneven
// terrain (see the old DecalProjector-based version this replaced).
public class SelectionPrefab : MonoBehaviour
{
    [SerializeField] private Image _image;

    // Transform to scale via SetScale — separate from this GameObject's own root transform
    // since SelectionManager drives the root's position every frame (see
    // SelectionManager.ApplySelectionPosition), and a uniform scale set here should
    // persist independently of that.
    [SerializeField] private Transform _scalableTransform;

    public bool IsSelected { get; private set; }

    void Awake()
    {
        if (_image == null)
            _image = GetComponentInChildren<Image>();
    }

    // Called once by SelectionManager right after instantiation (see SetupSelection), using
    // SelectableComponent.Scale.
    public void SetScale(float scale)
    {
        if (_scalableTransform != null)
            _scalableTransform.localScale = Vector3.one * scale;
    }

    public void SetSelected(Color color)
    {
        IsSelected = true;
        _image.color = color;
    }

    public void SetUnselected(Color color)
    {
        IsSelected = false;
        _image.color = color;
    }
}
