using UnityEngine;

// Flat world-space mesh plane shown under a troop/building while it's mid-deploy
// (TroopComponent's activation delay hasn't finished), rendered via a ShaderGraph material
// exposing a "_DeployProgress" float property (the world is flat, so a plain mesh plane
// lying on the ground is enough — no billboarding needed). DeployProgressIndicatorManager
// owns showing/hiding/positioning/updating every instance; this just owns the per-instance
// material, mirroring RangeIndicatorPrefab.
public class DeployProgressIndicatorPrefab : MonoBehaviour
{
    [SerializeField] private MeshRenderer _meshRenderer;
    // Must match the exposed reference name of the float property in the ShaderGraph.
    [SerializeField] private string _progressProperty = "_DeployProgress";

    private Material _material;

    void Awake()
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponentInChildren<MeshRenderer>();

        // Instance the material so setting progress here doesn't affect other indicators
        // sharing the same ShaderGraph material asset.
        if (_meshRenderer != null)
        {
            _meshRenderer.material = new Material(_meshRenderer.sharedMaterial);
            _material = _meshRenderer.material;
        }
    }

    // progress is remaining ticks until active divided by initial ticks until active — 1 at
    // spawn, counting down to 0 as the deploy delay finishes.
    public void SetProgress(float progress)
    {
        if (_material != null)
            _material.SetFloat(_progressProperty, progress);
    }
}
