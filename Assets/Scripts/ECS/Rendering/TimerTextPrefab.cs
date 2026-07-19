using TMPro;
using UnityEngine;

// Drives a single world-space countdown label. EntityTimerTextRenderer owns positioning and
// text content; this component just applies whatever it's told and keeps the label facing
// the active camera — mirrors HealthBarPrefab.
public class TimerTextPrefab : MonoBehaviour
{
    [SerializeField] private TMP_Text _text;
    [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 2.5f, 0f);

    public Vector3 WorldOffset => _worldOffset;

    public void SetText(string text)
    {
        if (_text != null)
            _text.text = text;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;
    }
}
