using UnityEngine;

// Generic expanding-circle VFX: instances a material from _shader, drives _durationProperty
// on it from 0 to 1 over _duration seconds while growing the circle's own scale from 0 to
// _finalSize in lockstep, then destroys itself. Not tied to any game-specific concept —
// reusable anywhere a "ring/circle bursts outward" effect is wanted (e.g. as the _prefab on
// PeriodicDamageReductionProcRenderer). Mirrors AoeSpellPrefab/RangeIndicatorPrefab's own
// flat-mesh-plane-on-the-ground convention: the prefab's plane mesh should be authored at 1
// unit diameter so _finalSize maps 1:1, and only X/Z are scaled (Y left at 1, no meaningful
// thickness).
public class ExpandingCirclePrefab : MonoBehaviour
{
    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] private Color _color = Color.white;
    [SerializeField] private Shader _shader;
    // Must match the exposed reference name of the float property in the ShaderGraph that
    // should be driven 0 -> 1 over _duration.
    [SerializeField] private string _durationProperty = "_DurationElapsed";
    [SerializeField] private float _duration = 1f;
    [SerializeField] private float _finalSize = 5f;

    // Must match the exposed reference name of the color property in the ShaderGraph — see
    // RangeIndicatorPrefab's own default, which this project's shader graphs follow.
    private const string ColorProperty = "_Color";

    private Material _material;
    private float _elapsed;

    void Awake()
    {
        if (_meshRenderer == null)
            _meshRenderer = GetComponentInChildren<MeshRenderer>();

        if (_meshRenderer != null && _shader != null)
        {
            _material = new Material(_shader);
            _material.SetColor(ColorProperty, _color);
            _meshRenderer.material = _material;
        }

        transform.localScale = new Vector3(0f, 0f, 0f);
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;

        if (_material != null && !string.IsNullOrEmpty(_durationProperty))
            _material.SetFloat(_durationProperty, t);

        float size = Mathf.Lerp(0f, _finalSize, t);
        transform.localScale = new Vector3(size, size, size);

        if (t >= 1f)
            Destroy(gameObject);
    }
}
