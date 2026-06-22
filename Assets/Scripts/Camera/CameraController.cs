using UnityEngine;

/// <summary>
/// LoL-style RTS camera. Attach directly to the Camera GameObject.
///
/// Controls:
///   Cursor near screen edge       — pan
///   Space + Left Mouse drag       — pan
///   Space + Right Mouse drag      — rotate around world Y axis
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("View")]
    [SerializeField] float _pitch = 50f;
    [SerializeField] float _distance = 20f;

    [Header("Edge Scroll")]
    [SerializeField] float _edgeScrollSpeed = 30f;
    [SerializeField][Range(0f, 0.15f)] float _edgeThreshold = 0.05f;

    [Header("Drag Pan")]
    [SerializeField] float _dragPanSensitivity = 0.5f;

    [Header("Rotation")]
    [SerializeField] float _rotateSensitivity = 0.3f;

    Vector3 _pivot;
    float _yaw;

    void Start()
    {
        _yaw = transform.eulerAngles.y;
        // Back-compute the pivot from the initial camera transform so the
        // scene-view placement is respected when entering play mode.
        _pivot = transform.position + transform.forward * _distance;
        _pivot.y = 0f;
    }

    void LateUpdate()
    {
        bool spacePressed = Input.GetKey(KeyCode.Space);
        bool dragPanning  = spacePressed && Input.GetMouseButton(0);
        bool rotating     = spacePressed && Input.GetMouseButton(1);

        if (!spacePressed) HandleEdgeScroll();
        if (dragPanning)  HandleDragPan();
        if (rotating)     HandleRotation();

        ApplyTransform();
    }

    void HandleEdgeScroll()
    {
        Vector2 mouse = Input.mousePosition;
        float w = Screen.width;
        float h = Screen.height;
        Vector3 dir = Vector3.zero;

        if (mouse.x < w * _edgeThreshold)           dir -= WorldRight();
        if (mouse.x > w * (1f - _edgeThreshold))    dir += WorldRight();
        if (mouse.y < h * _edgeThreshold)            dir -= WorldForward();
        if (mouse.y > h * (1f - _edgeThreshold))     dir += WorldForward();

        if (dir != Vector3.zero)
            _pivot += dir.normalized * _edgeScrollSpeed * Time.deltaTime;
    }

    void HandleDragPan()
    {
        // GetAxis("Mouse X/Y") returns pixel delta this frame — no Time.deltaTime needed.
        _pivot -= WorldRight()   * Input.GetAxis("Mouse X") * _dragPanSensitivity;
        _pivot -= WorldForward() * Input.GetAxis("Mouse Y") * _dragPanSensitivity;
    }

    void HandleRotation()
    {
        _yaw += Input.GetAxis("Mouse X") * _rotateSensitivity;
    }

    void ApplyTransform()
    {
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.rotation = rot;
        transform.position = _pivot - rot * Vector3.forward * _distance;
    }

    // Horizontal directions relative to current yaw so panning always feels
    // aligned with the screen regardless of rotation.
    Vector3 WorldForward() => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
    Vector3 WorldRight()   => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
}
