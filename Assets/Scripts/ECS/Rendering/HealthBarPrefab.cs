using UnityEngine;
using UnityEngine.UI;

// Drives a single world-space health bar. HealthBarManager owns positioning (from
// PositionComponent) and fill updates (from DamageDealtEvent); this component just
// applies whatever it's told and keeps the bar facing the active camera.
public class HealthBarPrefab : MonoBehaviour
{
    [SerializeField] private Image _fillImage;
    [SerializeField] private Vector3 _worldOffset = new Vector3(0, 2f, 0);

    public Vector3 WorldOffset => _worldOffset;

    public void SetFillAmount(float normalizedHealth)
    {
        _fillImage.fillAmount = Mathf.Clamp01(normalizedHealth);
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.forward = transform.position - cam.transform.position;
    }
}
