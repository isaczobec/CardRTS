using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Builds a one-pixel-per-tile minimap texture from each tile's TileTextureEntry.MapColor
// (via WorldManager.Renderer.TextureRegistry) once world gen finishes, then keeps a small
// screen-space dot positioned over the minimap for every entity with both
// SelectableComponent and PositionComponent (troops, buildings, trees), colored by
// ownership. Also keeps a viewport-indicator rectangle moved/rotated to
// track the main camera's current look-at point and yaw. Left-click (or click-drag) on the
// minimap jumps the main camera there, updating continuously every frame while held.
// Right-click issues the same target/move command right-clicking the 3D world would (see
// SelectionManager.PerformPointTargetOrMove).
//
// Mirrors SelectionManager's entity lifecycle (TroopActivatedEvent /
// ComponentRemovedEvent<SelectableComponent> / EntityDeletedEvent /
// RespawnableEntityDied/Respawned) and HealthBarManager's per-frame position refresh, but
// intentionally skips TickPositionInterpolator — dots just snap to the latest simulated
// position each tick; no need for the smooth between-tick interpolation the 3D world
// visuals use.
//
// Must sit on the same GameObject as the RawImage assigned to _mapImage — IPointerDownHandler/
// IPointerUpHandler only receive events when this component shares a GameObject with a
// raycastable Graphic.
[RequireComponent(typeof(RawImage))]
public class MinimapManager : Singleton<MinimapManager>, IPointerDownHandler, IPointerUpHandler
{
    [Header("Map")]
    [SerializeField] private RawImage _mapImage;

    [Header("Dots")]
    [SerializeField] private GameObject _dotPrefab;
    [SerializeField] private Vector2 _dotSize = new Vector2(6f, 6f);

    [Header("Colors")]
    [SerializeField] private Color _friendlyColor = Color.green;
    [SerializeField] private Color _neutralColor = Color.gray;
    [SerializeField] private Color _enemyColor = Color.red;

    [Header("Camera")]
    [SerializeField] private CameraController _cameraController;
    // Simple prefab (a bordered rectangle, texture/masking handled outside this script) —
    // just repositioned/rotated each frame to track the camera, never resized.
    [SerializeField] private GameObject _viewportIndicatorPrefab;

    private RectTransform _mapRect;
    private RectTransform _viewportIndicatorRect;
    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<SelectableComponent> _selectableStore;

    // Tiles per side of the (square) world — set once BuildMapTexture runs.
    private ushort _worldSizeTiles;

    private readonly Dictionary<ulong, RectTransform> _dots = new();

    // True from OnPointerDown until OnPointerUp — Update() re-jumps the camera to the
    // pointer's current position on the minimap every frame while this is set, giving the
    // "hold left-click and drag" continuous-follow behavior.
    private bool _isDraggingCamera;
    private Camera _dragEventCamera;

    protected override void Awake()
    {
        base.Awake();

        if (_mapImage == null)
            _mapImage = GetComponent<RawImage>();
        _mapRect = _mapImage.rectTransform;
    }

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<TroopActivatedEvent>(OnTroopActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<SelectableComponent>>(OnSelectableRemoved);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityDiedEvent>(OnRespawnableEntityDied);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityRespawnedEvent>(OnRespawnableEntityRespawned);

        _ecs = TickManager.instance.ActiveECS;
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _selectableStore = _ecs.GetComponentStore<SelectableComponent>();

        BuildMapTexture();
        SpawnViewportIndicator();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        foreach (KeyValuePair<ulong, RectTransform> kvp in _dots)
        {
            ulong entityId = kvp.Key;
            if (!_positionStore.HasComponent(entityId)) continue;
            ApplyDotPosition(_positionStore.GetComponent(entityId), kvp.Value);
        }

        UpdateViewportIndicator();

        // Re-derive the jump target from the live cursor position every frame (rather than
        // only reacting to pointer-move deltas via IDragHandler) so holding the button
        // still down without moving keeps the camera locked to that spot, and so a single
        // click-without-drag is already covered by OnPointerDown.
        if (_isDraggingCamera)
            DragCameraToPointer();
    }

    // ── Map texture ─────────────────────────────────────────────────────────

    private void BuildMapTexture()
    {
        if (_mapImage == null || WorldManager.instance == null || WorldManager.instance.Handler == null) return;

        TileTextureRegistry textureRegistry = WorldManager.instance.Renderer.TextureRegistry;
        if (textureRegistry == null) return;

        WorldGenHandler handler = WorldManager.instance.Handler;
        _worldSizeTiles = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks);

        Color32[] pixels = new Color32[_worldSizeTiles * _worldSizeTiles];
        for (ushort y = 0; y < _worldSizeTiles; y++)
        {
            for (ushort x = 0; x < _worldSizeTiles; x++)
            {
                TileType type = handler.GetTileType(x, y);
                pixels[y * _worldSizeTiles + x] = textureRegistry.GetMapColor(type);
            }
        }

        Texture2D texture = new Texture2D(_worldSizeTiles, _worldSizeTiles, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels32(pixels);
        texture.Apply();

        _mapImage.texture = texture;
    }

    // ── Dot lifecycle ───────────────────────────────────────────────────────

    private void OnTroopActivated(TroopActivatedEvent e)
    {
        if (_dots.ContainsKey(e.EntityId)) return;
        if (!_selectableStore.HasComponent(e.EntityId)) return;
        if (!_positionStore.HasComponent(e.EntityId)) return;
        if (_dotPrefab == null || _mapRect == null) return;

        // Parented directly under the map's own RectTransform so a (0,0) child anchor maps
        // 1:1 onto the map's own 0..1 UV span, regardless of the map's pivot — see
        // ApplyDotPosition. Being a later sibling of the RawImage also means it draws on
        // top of the map.
        GameObject go = Instantiate(_dotPrefab, _mapRect);
        go.name = $"MinimapDot_{e.EntityId}";

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = _dotSize;

        Image image = go.GetComponent<Image>();
        if (image != null)
            image.color = GetColor(e.EntityId);

        _dots[e.EntityId] = rect;
        ApplyDotPosition(_positionStore.GetComponent(e.EntityId), rect);
    }

    private void OnSelectableRemoved(ComponentRemovedEvent<SelectableComponent> e) => DestroyDot(e.EntityId);
    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyDot(e.EntityId);

    private void OnRespawnableEntityDied(RespawnableEntityDiedEvent e)
    {
        if (_dots.TryGetValue(e.EntityId, out RectTransform rect))
            rect.gameObject.SetActive(false);
    }

    private void OnRespawnableEntityRespawned(RespawnableEntityRespawnedEvent e)
    {
        if (_dots.TryGetValue(e.EntityId, out RectTransform rect))
            rect.gameObject.SetActive(true);
    }

    private void DestroyDot(ulong entityId)
    {
        if (!_dots.TryGetValue(entityId, out RectTransform rect)) return;
        Destroy(rect.gameObject);
        _dots.Remove(entityId);
    }

    private void ApplyDotPosition(PositionComponent pos, RectTransform rect)
        => rect.anchoredPosition = WorldToMinimapAnchoredPosition(pos.X, pos.Y);

    // pos.X/Y are world/tile-space (see PositionComponent) — the same axes BuildMapTexture
    // painted the map texture in, so a straight 0..worldSizeTiles -> 0..mapRect.size
    // normalization lines dots (and the viewport indicator) up with the terrain under them.
    private Vector2 WorldToMinimapAnchoredPosition(float worldX, float worldZ)
    {
        if (_worldSizeTiles == 0) return Vector2.zero;

        float u = Mathf.Clamp01(worldX / _worldSizeTiles);
        float v = Mathf.Clamp01(worldZ / _worldSizeTiles);

        Rect mapRect = _mapRect.rect;
        return new Vector2(u * mapRect.width, v * mapRect.height);
    }

    // Mirrors SelectionManager.GetUnselectedColor's ownership check.
    private Color GetColor(ulong entityId)
    {
        ushort owner = _selectableStore.GetComponent(entityId).OwnerPlayerId;
        if (owner == LocalPlayerId()) return _friendlyColor;
        if (owner == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return _neutralColor;
        return _enemyColor;
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    // ── Viewport indicator ──────────────────────────────────────────────────

    private void SpawnViewportIndicator()
    {
        if (_viewportIndicatorPrefab == null || _mapRect == null) return;

        GameObject go = Instantiate(_viewportIndicatorPrefab, _mapRect);
        go.name = "MinimapViewportIndicator";

        _viewportIndicatorRect = go.GetComponent<RectTransform>();
        _viewportIndicatorRect.anchorMin = _viewportIndicatorRect.anchorMax = Vector2.zero;
        // Left at whatever size/rotation the prefab was authored with — this script only
        // ever moves and rotates it, per the "simple prefab, I'll handle the texture and
        // masking" ask.
    }

    private void UpdateViewportIndicator()
    {
        if (_viewportIndicatorRect == null || _cameraController == null) return;

        Vector3 pivot = _cameraController.Pivot;
        _viewportIndicatorRect.anchoredPosition = WorldToMinimapAnchoredPosition(pivot.x, pivot.z);

        // World yaw rotates +Z (map "up") toward +X (map "right") as it increases — i.e.
        // clockwise viewed from above, which matches the minimap's un-mirrored top-down
        // orientation. UI Z+ rotation is counter-clockwise on screen, so negate to match.
        // Assumes the prefab is authored facing "up" (0 rotation); flip the sign here if
        // yours points the other way.
        _viewportIndicatorRect.localEulerAngles = new Vector3(0f, 0f, -_cameraController.Yaw);
    }

    // ── Left-click/drag: move camera ────────────────────────────────────────

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            IssueMoveOrTargetCommand(eventData);
            return;
        }
        if (eventData.button != PointerEventData.InputButton.Left) return;

        _isDraggingCamera = true;
        _dragEventCamera = eventData.pressEventCamera;
        JumpCameraToScreenPoint(eventData.position);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        _isDraggingCamera = false;
    }

    private void DragCameraToPointer() => JumpCameraToScreenPoint(Input.mousePosition);

    private void JumpCameraToScreenPoint(Vector2 screenPoint)
    {
        if (_cameraController == null) return;
        if (!TryScreenPointToWorld(screenPoint, _dragEventCamera, out float worldX, out float worldZ)) return;

        _cameraController.JumpTo(new Vector3(worldX, 0f, worldZ));
    }

    // ── Right-click: target/move the current selection ─────────────────────

    // Works exactly like right-clicking the 3D world (see SelectionManager.
    // HandleRightClickInput) — targets whatever's under the point if anything is, else
    // issues a move command — just resolving its world position from a minimap click
    // instead of a ground raycast.
    private void IssueMoveOrTargetCommand(PointerEventData eventData)
    {
        if (SelectionManager.instance == null) return;
        if (!TryScreenPointToWorld(eventData.position, eventData.pressEventCamera, out float worldX, out float worldZ)) return;

        SelectionManager.instance.PerformPointTargetOrMove(worldX, worldZ);
    }

    private bool TryScreenPointToWorld(Vector2 screenPoint, Camera eventCamera, out float worldX, out float worldZ)
    {
        worldX = worldZ = 0f;
        if (_mapRect == null || _worldSizeTiles == 0) return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapRect, screenPoint, eventCamera, out Vector2 local))
            return false;

        Rect r = _mapRect.rect;
        float u = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
        float v = Mathf.InverseLerp(r.yMin, r.yMax, local.y);

        worldX = u * _worldSizeTiles;
        worldZ = v * _worldSizeTiles;
        return true;
    }
}
