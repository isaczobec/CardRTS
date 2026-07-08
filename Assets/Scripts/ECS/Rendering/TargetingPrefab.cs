using UnityEngine;
using UnityEngine.Rendering.Universal;

// Decal shown on a troop while it is targeted by at least one friendly troop.
// Managed the same way as SelectionPrefab (instantiated per troop, repositioned every
// frame to follow it), but stays hidden until SetTargeted is called. Colors itself
// based on whether the target was player-assigned or picked automatically by AI.
public class TargetingPrefab : MonoBehaviour
{
    [SerializeField] private DecalProjector _decalProjector;
    [SerializeField] private Color _playerAssignedColor = Color.red;
    [SerializeField] private Color _automaticColor = Color.yellow;

    public bool IsTargeted { get; private set; }
    public TargetKind? CurrentKind { get; private set; }

    void Awake()
    {
        if (_decalProjector == null)
            _decalProjector = GetComponentInChildren<DecalProjector>();

        _decalProjector.renderingLayerMask = WorldManager.instance.Renderer.TerrainRenderingLayerMask;

        // Instance the material so setting color properties doesn't affect other decals.
        _decalProjector.material = new Material(_decalProjector.material);

        gameObject.SetActive(false);
    }

    public void SetTargeted(TargetKind kind, string colorProperty)
    {
        IsTargeted = true;
        CurrentKind = kind;
        Color color = kind == TargetKind.Automatic ? _automaticColor : _playerAssignedColor;
        _decalProjector.material.SetColor(colorProperty, color);
        gameObject.SetActive(true);
    }

    public void SetUntargeted()
    {
        IsTargeted = false;
        CurrentKind = null;
        gameObject.SetActive(false);
    }
}
