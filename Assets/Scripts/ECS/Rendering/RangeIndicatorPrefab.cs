using UnityEngine;

// Flat world-space mesh plane marking how far from a building a card can be played,
// rendered via a ShaderGraph material (the world is flat, so a plain mesh plane lying on
// the ground is enough — no billboarding needed). CardRangeIndicatorManager owns showing/
// hiding/scaling/positioning every instance; this just owns the per-instance material.
public class RangeIndicatorPrefab : MonoBehaviour
{
    [SerializeField] private MeshRenderer _meshRenderer;
    // Must match the exposed reference name of the color property in the ShaderGraph.
    [SerializeField] private string _colorProperty = "_Color";

    private Material _material;

    void Awake()
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponentInChildren<MeshRenderer>();

        // Instance the material so setting color here doesn't affect other indicators
        // sharing the same ShaderGraph material asset.
        if (_meshRenderer != null)
        {
            _meshRenderer.material = new Material(_meshRenderer.sharedMaterial);
            _material = _meshRenderer.material;
        }
    }

    // scale is a diameter (in world units) — the prefab's plane mesh should be authored at
    // 1 unit diameter so this maps 1:1. Only X/Z are scaled; Y is left alone since the
    // plane has no meaningful thickness.
    public void SetScale(float scale) => transform.localScale = new Vector3(scale, 1f, scale);

    public void SetColor(Color color)
    {
        if (_material != null)
            _material.SetColor(_colorProperty, color);
    }
}
