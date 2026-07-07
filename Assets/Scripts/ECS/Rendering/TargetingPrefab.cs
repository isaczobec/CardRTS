using UnityEngine;
using UnityEngine.Rendering.Universal;

// Decal shown on a troop while it is targeted by at least one friendly troop.
// Managed the same way as SelectionPrefab (instantiated per troop, repositioned every
// frame to follow it), but stays hidden until SetTargeted is called.
public class TargetingPrefab : MonoBehaviour
{
    [SerializeField] private DecalProjector _decalProjector;

    public bool IsTargeted { get; private set; }

    void Awake()
    {
        if (_decalProjector == null)
            _decalProjector = GetComponentInChildren<DecalProjector>();

        _decalProjector.renderingLayerMask = WorldManager.instance.Renderer.TerrainRenderingLayerMask;

        // Instance the material so setting color properties doesn't affect other decals.
        _decalProjector.material = new Material(_decalProjector.material);

        gameObject.SetActive(false);
    }

    public void SetTargeted(Color color, string colorProperty)
    {
        IsTargeted = true;
        _decalProjector.material.SetColor(colorProperty, color);
        gameObject.SetActive(true);
    }

    public void SetUntargeted()
    {
        IsTargeted = false;
        gameObject.SetActive(false);
    }
}
