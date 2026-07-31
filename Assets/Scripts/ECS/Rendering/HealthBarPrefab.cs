using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Drives a single world-space health bar. HealthBarManager owns positioning (from
// PositionComponent) and health updates (from DamageRequest's SubscribeExecuted callback —
// see HealthBarManager.OnDamageRequestExecuted); this component just applies whatever it's
// told and keeps the bar facing the active camera. The fill is drawn entirely by _material's
// shader (Assets/Shaders/HealthBar.shader) off _CurrentHealthNormalized/_MaxHealth — there's
// no separate background/fill Image hierarchy or Image.fillAmount masking anymore.
public class HealthBarPrefab : MonoBehaviour
{
    [SerializeField] private Image _image;
    // Optional — shows "<current>/<max>" over the fill. Left null on a bar that doesn't
    // want the text (e.g. a smaller/simpler variant prefab).
    [SerializeField] private TMP_Text _healthText;
    // Template material using the HealthBar shader — instantiated fresh per bar in Awake so
    // each troop's fill/segment ticks can be driven independently rather than all bars
    // fighting over one shared material's property values.
    [SerializeField] private Material _material;
    [SerializeField] private Vector3 _worldOffset = new Vector3(0, 2f, 0);

    [Header("Width vs. Max Health")]
    // The RectTransform whose width grows with MaxHealth — usually the same object _image
    // lives on, but kept as its own reference rather than assumed, in case the bar's
    // background/frame is a separate parent RectTransform from the Image itself.
    [SerializeField] private RectTransform _rectTransformToScale;
    [SerializeField] private float _baseWidth = 50f;
    [SerializeField] private float _widthScale = 5f;
    // Power curve exponent: 1 = linear (width scales directly with MaxHealth), 0.5 = square
    // root (a power function, so — unlike a logarithm — it keeps growing at a roughly
    // constant relative rate rather than flattening out at high health, while still
    // compressing the spread compared to linear), toward 0 = flatter/more compressed. A
    // single, direct knob: doubling MaxHealth always widens the bar by exactly
    // 2^_widthExponent, regardless of the current health value — much easier to reason
    // about/tune than a logarithm's multiplier, whose effect on the resulting range isn't
    // as directly predictable.
    [SerializeField] private float _widthExponent = 0.5f;
    [SerializeField] private float _minWidth = 20f;

    [Header("Scale vs. Camera Height")]
    // Multiplier applied on TOP of this bar's own authored (prefab) localScale — never an
    // absolute override — so whatever baseline scale the prefab was set up with (e.g. a
    // world-space canvas scaled down to convert pixel-sized UI into world units) is always
    // preserved. 1 = no change at camera height 0.
    [SerializeField] private float _baseScaleMultiplier = 1f;
    // Multiplier gained (or lost, if negative) per world unit of Camera.main's height
    // (transform.position.y). Positive values grow bars as the camera pulls back/zooms
    // out, compensating for perspective shrinking distant objects so bars stay legible at
    // any zoom; 0 = no height-based scaling at all (bars always render at their authored
    // size, today's behavior).
    [SerializeField] private float _scalePerHeightUnit = 0f;
    [SerializeField] private float _minScaleMultiplier = 0.1f;

    private Vector3 _baseLocalScale;

    private static readonly int CurrentHealthNormalizedId = Shader.PropertyToID("_CurrentHealthNormalized");
    private static readonly int MaxHealthId = Shader.PropertyToID("_MaxHealth");

    // SetHealth is called on every damage tick, but MaxHealth itself only ever changes at
    // spawn (or later, rarely, from a MaxHealth stat modifier) — this skips re-touching
    // RectTransform.sizeDelta (which dirties layout) on every one of those calls when the
    // width wouldn't actually change.
    private int _lastMaxHealthForWidth = -1;

    public Vector3 WorldOffset => _worldOffset;

    void Awake()
    {
        _material = Instantiate(_material);
        _image.material = _material;
        _baseLocalScale = transform.localScale;
    }

    public void SetHealth(int currentHealth, int maxHealth)
    {
        float normalized = maxHealth > 0 ? Mathf.Clamp01((float)currentHealth / maxHealth) : 0f;
        _material.SetFloat(CurrentHealthNormalizedId, normalized);
        _material.SetFloat(MaxHealthId, Mathf.Max(0, maxHealth));

        if (_healthText != null)
            _healthText.text = $"{Mathf.Max(0, currentHealth)}/{Mathf.Max(0, maxHealth)}";

        ApplyWidthForMaxHealth(maxHealth);
    }

    // Width grows with MaxHealth^_widthExponent rather than linearly with it, so a troop
    // with e.g. 10x the health of another reads as noticeably-but-not-10x wider — bars stay
    // readable/comparable across a wide spread of health pools instead of a high-health
    // troop's bar dwarfing everything else on screen, while still growing faster (and
    // staying more directly tunable via _widthExponent alone) than a logarithm would.
    private void ApplyWidthForMaxHealth(int maxHealth)
    {
        if (_rectTransformToScale == null) return;
        if (maxHealth == _lastMaxHealthForWidth) return;
        _lastMaxHealthForWidth = maxHealth;

        float width = Mathf.Max(_minWidth, _baseWidth + _widthScale * Mathf.Pow(Mathf.Max(0, maxHealth), _widthExponent));

        Vector2 size = _rectTransformToScale.sizeDelta;
        size.x = width;
        _rectTransformToScale.sizeDelta = size;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;

        float multiplier = Mathf.Max(_minScaleMultiplier, _baseScaleMultiplier + _scalePerHeightUnit * cam.transform.position.y);
        transform.localScale = _baseLocalScale * multiplier;
    }

    // The instantiated material isn't a scene asset Unity tracks/destroys on its own —
    // without this it'd leak one Material object per health bar for the life of the process.
    void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
