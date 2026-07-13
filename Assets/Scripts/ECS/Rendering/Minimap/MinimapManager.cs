using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Builds a one-pixel-per-tile minimap texture from each tile's TileTextureEntry.MapColor
// (via WorldManager.Renderer.TextureRegistry) once world gen finishes, then keeps a small
// screen-space dot positioned over the minimap for
// every entity with both SelectableComponent and PositionComponent (troops, buildings,
// trees), colored by ownership. Clicking the minimap jumps the main camera there.
//
// Mirrors SelectionManager's entity lifecycle (TroopActivatedEvent /
// ComponentRemovedEvent<SelectableComponent> / EntityDeletedEvent /
// RespawnableEntityDied/Respawned) and HealthBarManager's per-frame position refresh, but
// intentionally skips TickPositionInterpolator — dots just snap to the latest simulated
// position each tick; no need for the smooth between-tick interpolation the 3D world
// visuals use.
//
// Must sit on the same GameObject as the RawImage assigned to _mapImage — IPointerClickHandler
// only receives events when this component shares a GameObject with a raycastable Graphic.
[RequireComponent(typeof(RawImage))]
public class MinimapManager : Singleton<MinimapManager>, IPointerClickHandler
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

    private RectTransform _mapRect;
    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<SelectableComponent> _selectableStore;

    // Tiles per side of the (square) world — set once BuildMapTexture runs.
    private ushort _worldSizeTiles;

    private readonly Dictionary<ulong, RectTransform> _dots = new();

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
    {
        if (_worldSizeTiles == 0) return;

        float u = Mathf.Clamp01(pos.X / _worldSizeTiles);
        float v = Mathf.Clamp01(pos.Y / _worldSizeTiles);

        Rect mapRect = _mapRect.rect;
        rect.anchoredPosition = new Vector2(u * mapRect.width, v * mapRect.height);
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

    // ── Click-to-move-camera ────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_mapRect == null || _cameraController == null || _worldSizeTiles == 0) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;

        Rect r = _mapRect.rect;
        float u = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
        float v = Mathf.InverseLerp(r.yMin, r.yMax, local.y);

        float worldX = u * _worldSizeTiles;
        float worldY = v * _worldSizeTiles;
        _cameraController.JumpTo(new Vector3(worldX, 0f, worldY));
    }
}
