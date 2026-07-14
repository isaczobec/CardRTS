using UnityEngine;

// Flat world-space mesh plane for an active AOE spell entity, rendered via a ShaderGraph
// material exposing a "_DurationElapsed" float property (0 right when it activates, 1 once
// its LifetimeComponent countdown finishes) — the world is flat, so a plain mesh plane
// lying on the ground is enough, no billboarding needed. AoeSpellRenderer owns showing/
// positioning/scaling/updating every instance; this just owns the per-instance material,
// mirroring RangeIndicatorPrefab/DeployProgressIndicatorPrefab.
public class AoeSpellPrefab : MonoBehaviour
{
    [SerializeField] private MeshRenderer _meshRenderer;
    // Must match the exposed reference name of the float property in the ShaderGraph.
    [SerializeField] private string _durationElapsedProperty = "_DurationElapsed";

    // Remaps the raw 0→1 elapsed-lifetime fraction (see SetDurationElapsed) before it's
    // written to the material — e.g. easing the ramp instead of a linear 0→1, or holding
    // near 0 before rising. Defaults to a straight line (t -> t) so it's a no-op until
    // configured on the prefab.
    [SerializeField] private AnimationCurve _durationElapsedCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private Material _material;

    void Awake()
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponentInChildren<MeshRenderer>();

        // Instance the material so setting the property here doesn't affect other AOE
        // spell instances sharing the same ShaderGraph material asset.
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

    public void SetDurationElapsed(float value)
    {
        if (_material != null)
            _material.SetFloat(_durationElapsedProperty, _durationElapsedCurve.Evaluate(value));
    }
}
