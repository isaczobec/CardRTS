using UnityEngine;
using UnityEngine.Rendering.Universal;

public class SelectionPrefab : MonoBehaviour
{
    [SerializeField] private DecalProjector _decalProjector;

    public bool IsSelected { get; private set; }

    void Awake()
    {
        if (_decalProjector == null)
            _decalProjector = GetComponentInChildren<DecalProjector>();

        _decalProjector.renderingLayerMask = WorldManager.instance.Renderer.TerrainRenderingLayerMask;

        // Instance the material so setting color properties doesn't affect other decals.
        _decalProjector.material = new Material(_decalProjector.material);
    }

    public void SetSelected(Color color, string colorProperty)
    {
        IsSelected = true;
        _decalProjector.material.SetColor(colorProperty, color);
    }

    public void SetUnselected(Color color, string colorProperty)
    {
        IsSelected = false;
        _decalProjector.material.SetColor(colorProperty, color);
    }
}
