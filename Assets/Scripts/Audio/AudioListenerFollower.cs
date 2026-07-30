using UnityEngine;

/// <summary>
/// Keeps this GameObject (and whatever AudioListener is on it) directly beneath _target at a
/// fixed world-space elevation. Exists so the listener's distance to 3D sound sources stays
/// consistent as the camera's own height/tilt changes (e.g. CameraController's scroll-wheel
/// zoom) — an AudioListener parented straight onto the camera would otherwise make every sound
/// louder when zoomed in and quieter when zoomed out, on top of whatever distance attenuation
/// is already meant to convey.
/// </summary>
public class AudioListenerFollower : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private float _elevation = 0f;

    void LateUpdate()
    {
        if (_target == null) return;

        Vector3 targetPos = _target.position;
        transform.position = new Vector3(targetPos.x, _elevation, targetPos.z);
    }
}
