using UnityEngine;

/// <summary>
/// LoL-style RTS camera. Attach directly to the Camera GameObject.
///
/// Controls:
///   Cursor near screen edge       — pan
///   Left Alt + Left Mouse drag    — pan
///   Left Alt + Right Mouse drag   — rotate around world Y axis
///   Tab                           — jump to the next entity the local player owns (see
///                                    SelectionManager.TryGetNextOwnedEntityPosition)
///   Shift + Tab                   — jump to the previous one instead
///   Space (held)                  — continuously follows the current selection every
///                                    frame (see SelectionManager.TryGetFocusPositionForSelection),
///                                    suppressing edge-scroll while it's actively tracking
///   Scroll wheel                  — raise/lower the camera's elevation, clamped between
///                                    _minDistance and _maxDistance
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
    // Max gap (seconds) between two Left Alt key-downs for the second one to count as a
    // double-tap and reset the camera's yaw back to _initialYaw — same "quick succession"
    // feel as a double-click.
    [SerializeField] float _doubleTapAltResetWindow = 0.3f;

    [Header("Zoom")]
    [SerializeField] float _zoomSensitivity = 15f;
    [SerializeField] float _minDistance = 8f;
    [SerializeField] float _maxDistance = 40f;

    Vector3 _pivot;
    float _yaw;
    float _initialYaw;
    float _lastAltPressTime = -1f;

    // Read-only access for MinimapManager's viewport indicator (position + rotation).
    public Vector3 Pivot => _pivot;
    public float Yaw => _yaw;

    void Start()
    {
        _yaw = transform.eulerAngles.y;
        _initialYaw = _yaw;
        // Back-compute the pivot from the initial camera transform so the
        // scene-view placement is respected when entering play mode.
        _pivot = transform.position + transform.forward * _distance;
        _pivot.y = 0f;

        Cursor.lockState = CursorLockMode.Confined;

        GameEvents.OnGameStarting += MoveToMapCenter;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            Cursor.lockState = CursorLockMode.Confined;
    }

    void OnDestroy()
    {
        GameEvents.OnGameStarting -= MoveToMapCenter;
    }

    void MoveToMapCenter()
    {
        if (WorldManager.instance == null) return;
        ushort half = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks / 2);
        Vector3 center = WorldManager.instance.TileToWorldPosition(half, half, center: true);
        _pivot = new Vector3(center.x, 0f, center.z);
    }

    // Snaps the camera's look-at point to worldPos (e.g. from a minimap click) — same
    // ground-plane-pivot approach as MoveToMapCenter, just with an arbitrary target instead
    // of the map's center.
    public void JumpTo(Vector3 worldPos)
    {
        _pivot = new Vector3(worldPos.x, 0f, worldPos.z);
    }

    void LateUpdate()
    {
        if (!Application.isFocused) return;
        // A selection/targeting drag box is active — don't let edge-scroll (or anything
        // else) move the camera out from under an in-progress drag.
        if (SelectionManager.instance != null && SelectionManager.instance.IsDragActive) return;

        bool altPressed = Input.GetKey(KeyCode.LeftAlt);
        bool dragPanning = altPressed && Input.GetMouseButton(0);
        bool rotating    = altPressed && Input.GetMouseButton(1);

        bool following = HandleFollowSelection();

        // Edge-scroll would otherwise fight the follow every frame (re-centering, then
        // immediately getting nudged back off-center by the cursor sitting near an edge).
        if (!altPressed && !following) HandleEdgeScroll();
        if (dragPanning) HandleDragPan();
        if (rotating)    HandleRotation();

        HandleTabHotkey();
        HandleZoom();
        HandleAltDoubleTapReset();

        ApplyTransform();
    }

    // Double-tapping Left Alt (two key-downs within _doubleTapAltResetWindow) snaps yaw
    // back to whatever the camera started at, regardless of how far HandleRotation has
    // rotated it since — a quick "undo my rotation" shortcut. GetKeyDown (not GetKey) so
    // this only evaluates on the actual press edge, not every frame Alt is held (which
    // would keep resetting _lastAltPressTime and make a double-tap impossible to land).
    void HandleAltDoubleTapReset()
    {
        if (DevConsole.IsOpen) return;
        if (!Input.GetKeyDown(KeyCode.LeftAlt)) return;

        float now = Time.unscaledTime;
        bool isDoubleTap = _lastAltPressTime >= 0f && now - _lastAltPressTime <= _doubleTapAltResetWindow;

        if (isDoubleTap)
        {
            _yaw = _initialYaw;
            _lastAltPressTime = -1f; // consumed — a third tap starts a fresh pair, not another reset
        }
        else
        {
            _lastAltPressTime = now;
        }
    }

    // Held Space: re-centers the camera on the current selection every frame it's held,
    // tracking a moving troop instead of just jumping to wherever it was once. Guarded
    // against the dev console the same way Tab is (a message containing a space shouldn't
    // yank the camera around while typing). Returns whether it actually followed this
    // frame, so LateUpdate knows to suppress edge-scroll.
    bool HandleFollowSelection()
    {
        if (DevConsole.IsOpen) return false;
        if (!Input.GetKey(KeyCode.Space)) return false;
        if (SelectionManager.instance == null) return false;
        if (!SelectionManager.instance.TryGetFocusPositionForSelection(out Vector3 pos)) return false;

        JumpTo(pos);
        return true;
    }

    // Tab is a one-shot GetKeyDown trigger (unlike held Space above) — same dev-console
    // guard reasoning, though Tab itself can't be typed into the console's text field.
    // Shift+Tab cycles backward through the same ordering instead of forward.
    void HandleTabHotkey()
    {
        if (DevConsole.IsOpen) return;
        if (!Input.GetKeyDown(KeyCode.Tab)) return;
        if (SelectionManager.instance == null) return;

        bool forward = !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift);
        if (SelectionManager.instance.TryGetNextOwnedEntityPosition(out Vector3 nextOwnedPos, forward))
            JumpTo(nextOwnedPos);
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

    // Scroll up (positive delta) zooms in, lowering the camera's elevation; scroll down
    // raises it back up. _distance already doubles as elevation here since ApplyTransform
    // places the camera along a fixed pitch away from the pivot.
    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll == 0f) return;
        if (IsShopMenuOpen()) return;

        _distance = Mathf.Clamp(_distance - scroll * _zoomSensitivity, _minDistance, _maxDistance);
    }

    // Scrolling the shop/upgrade grid (see ScrollPanel) would otherwise also zoom the camera
    // underneath it, since Input.GetAxis("Mouse ScrollWheel") fires regardless of what's under
    // the cursor — simplest fix is just suppressing zoom entirely while either window is open.
    static bool IsShopMenuOpen()
    {
        if (ShopUIManager.instance != null && ShopUIManager.instance.IsOpen) return true;
        if (UpgradeShopUIManager.instance != null && UpgradeShopUIManager.instance.IsOpen) return true;
        return false;
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
