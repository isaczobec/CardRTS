using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Generic, reusable world-space floating text effect: rises and fades out over its
// lifetime, then destroys itself. Configure via Setup() right after instantiating (see
// FloatingTextManager for the damage-number/resource-gain usage). Not tied to any
// game-specific concept — reusable for anything that wants a floating "+N"/"-N" callout,
// optionally with an icon next to the text.
public class FloatingTextSpawner : MonoBehaviour
{
    [SerializeField] private TMP_Text _text;
    [SerializeField] private Image _icon;
    // Units/second — independent of the fade below, so rise and fade don't have to finish
    // at the same time (e.g. a slow drift with a quick fade, or vice versa).
    [SerializeField] private float _riseSpeed = 1.5f;
    // How long the fade curve takes to play out, in seconds — the curve's own X axis is
    // normalized 0..1 (0 = spawn, 1 = fully faded) and gets remapped against this duration.
    [SerializeField] private float _fadeDuration = 1f;
    [SerializeField] private AnimationCurve _fadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    private float _elapsed;

    public void Setup(string text, Color color, Sprite icon = null)
    {
        if (_text != null)
        {
            _text.text = text;
            _text.color = color;
        }

        if (_icon != null)
        {
            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }
    }

    void Update()
    {
        transform.position += Vector3.up * (_riseSpeed * Time.deltaTime);

        _elapsed += Time.deltaTime;
        float t = _fadeDuration > 0f ? Mathf.Clamp01(_elapsed / _fadeDuration) : 1f;
        float alpha = Mathf.Clamp01(_fadeCurve.Evaluate(t));

        SetAlpha(_text, alpha);
        if (_icon != null && _icon.enabled)
            SetAlpha(_icon, alpha);

        if (t >= 1f)
            Destroy(gameObject);
    }

    // Faces the camera every frame, same as HealthBarPrefab — this project's world-space
    // UI billboards rather than relying on a fixed canvas rotation.
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;
    }

    // TMP_Text and Image both derive from Graphic, which is all this needs.
    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null) return;
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }
}
