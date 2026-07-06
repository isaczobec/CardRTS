using System.Collections.Generic;
using UnityEngine;

public class SelectionManager : Singleton<SelectionManager>
{
    private const float SingleSelectRadius = 2f;
    private const float DragThresholdPixels = 5f;

    [SerializeField] private GameObject _selectionPrefab;
    // Optional: assign a RectTransform (pivot & anchor at bottom-left) for the drag rect visual.
    [SerializeField] private RectTransform _dragSelectionBox;

    [Header("Visuals")]
    [SerializeField] private Color _friendlyColor = Color.green;
    [SerializeField] private Color _friendlySelectedColor = Color.green;
    [SerializeField] private Color _friendlySingleSelectedColor = Color.green;
    [SerializeField] private Color _neutralColor = Color.green;
    [SerializeField] private Color _enemyColor = Color.green;
    [SerializeField] private string _selectionColorProperty = "_SelectionColor";


    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<SelectableComponent> _selectableStore;
    private EntityChunkTracker _chunkTracker;

    private readonly Dictionary<ulong, SelectionPrefab> _selectionObjects = new();
    private readonly HashSet<ulong> _selectedEntityIds = new();
    public IReadOnlyCollection<ulong> SelectedEntityIds => _selectedEntityIds;

    // Drag state
    private Vector2 _dragStartScreen;
    private float _dragStartTileX;
    private float _dragStartTileY;
    private bool _isDragging;

    private readonly List<ulong> _queryBuffer = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<SelectableComponent>>(SetupSelection);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<SelectableComponent>>(RemoveSelectionObject);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(DeleteSelectionObject);

        ECS ecs = TickManager.instance.ActiveECS;
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _selectableStore = ecs.GetComponentStore<SelectableComponent>();
        _chunkTracker = ecs.ChunkTracker;

        if (_dragSelectionBox != null)
            _dragSelectionBox.gameObject.SetActive(false);
    }

    public void Update()
    {
        foreach (var kvp in _selectionObjects)
        {
            ulong entityId = kvp.Key;
            SelectionPrefab selection = kvp.Value;

            if (_positionStore.HasComponent(entityId))
            {
                PositionComponent pos = _positionStore.GetComponent(entityId);
                ApplySelectionPosition(ref pos, selection);
            }
        }

        HandleSelectionInput();
    }

    // ── Input ────────────────────────────────────────────────────────────────

    private void HandleSelectionInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _dragStartScreen = Input.mousePosition;
            TileSpaceMouse.TryGetPosition(out _dragStartTileX, out _dragStartTileY);
            _isDragging = false;
        }

        if (Input.GetMouseButton(0))
        {
            Vector2 delta = (Vector2)Input.mousePosition - _dragStartScreen;
            if (!_isDragging && delta.magnitude > DragThresholdPixels)
            {
                _isDragging = true;
                if (_dragSelectionBox != null)
                    _dragSelectionBox.gameObject.SetActive(true);
            }

            if (_isDragging)
                UpdateDragBox();
        }

        if (Input.GetMouseButtonUp(0))
        {
            if (_dragSelectionBox != null)
                _dragSelectionBox.gameObject.SetActive(false);

            if (_isDragging)
                PerformRectSelect();
            else
                PerformPointSelect();

            _isDragging = false;
        }
    }

    // ── Selection logic ───────────────────────────────────────────────────────

    private ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    private bool IsFriendly(ulong entityId)
    {
        if (!_selectableStore.HasComponent(entityId)) return false;
        return _selectableStore.GetComponent(entityId).OwnerPlayerId == LocalPlayerId();
    }

    private Color GetUnselectedColor(ulong entityId)
    {
        if (!_selectableStore.HasComponent(entityId)) return _friendlyColor;
        ushort owner = _selectableStore.GetComponent(entityId).OwnerPlayerId;
        ushort localId = LocalPlayerId();
        if (owner == localId) return _friendlyColor;
        if (owner == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return _neutralColor;
        return _enemyColor;
    }

    private void PerformPointSelect()
    {
        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty))
        {
            DeselectAll();
            return;
        }

        _queryBuffer.Clear();
        _chunkTracker.GetEntitiesNear(tx, ty, SingleSelectRadius, _queryBuffer);

        ulong bestId = 0;
        float bestDist2 = SingleSelectRadius * SingleSelectRadius;

        foreach (ulong entityId in _queryBuffer)
        {
            if (!IsFriendly(entityId)) continue;
            PositionComponent pos = _positionStore.GetComponent(entityId);
            float dx = pos.X - tx, dy = pos.Y - ty;
            float d2 = dx * dx + dy * dy;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                bestId = entityId;
            }
        }

        DeselectAll();
        if (bestId != 0)
            Select(bestId, _friendlySingleSelectedColor);
    }

    private void PerformRectSelect()
    {
        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty)) return;

        float minX = Mathf.Min(_dragStartTileX, tx);
        float maxX = Mathf.Max(_dragStartTileX, tx);
        float minY = Mathf.Min(_dragStartTileY, ty);
        float maxY = Mathf.Max(_dragStartTileY, ty);

        _queryBuffer.Clear();
        _chunkTracker.GetEntitiesInRect(minX, minY, maxX, maxY, _queryBuffer);

        DeselectAll();
        foreach (ulong entityId in _queryBuffer)
        {
            if (!IsFriendly(entityId)) continue;
            Select(entityId, _friendlySelectedColor);
        }
    }

    private void Select(ulong entityId, Color color)
    {
        if (!_selectedEntityIds.Add(entityId)) return;
        if (_selectionObjects.TryGetValue(entityId, out var prefab))
            prefab.SetSelected(color, _selectionColorProperty);
    }

    private void DeselectAll()
    {
        foreach (ulong entityId in _selectedEntityIds)
            if (_selectionObjects.TryGetValue(entityId, out var prefab))
                prefab.SetUnselected(GetUnselectedColor(entityId), _selectionColorProperty);
        _selectedEntityIds.Clear();
    }

    // ── Drag box ─────────────────────────────────────────────────────────────

    private void UpdateDragBox()
    {
        if (_dragSelectionBox == null) return;
        Vector2 current = Input.mousePosition;
        Vector2 min = Vector2.Min(_dragStartScreen, current);
        Vector2 max = Vector2.Max(_dragStartScreen, current);
        // Force anchor and pivot to bottom-left so offsetMin/offsetMax map
        // directly to screen-pixel corners regardless of inspector settings.
        _dragSelectionBox.anchorMin = Vector2.zero;
        _dragSelectionBox.anchorMax = Vector2.zero;
        _dragSelectionBox.pivot = Vector2.zero;
        _dragSelectionBox.anchoredPosition = min;
        _dragSelectionBox.sizeDelta = max - min;
    }

    // ── Selection object lifecycle ────────────────────────────────────────────

    public void SetupSelection(ComponentAddedEvent<SelectableComponent> e)
    {
        if (_selectionObjects.ContainsKey(e.EntityId)) return;
        if (!_positionStore.HasComponent(e.EntityId)) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        GameObject go = Instantiate(_selectionPrefab, transform);
        go.name = $"Selection_{e.EntityId}";
        SelectionPrefab prefab = go.GetComponent<SelectionPrefab>();
        _selectionObjects[e.EntityId] = prefab;
        ApplySelectionPosition(ref pos, prefab);
        prefab.SetUnselected(GetUnselectedColor(e.EntityId), _selectionColorProperty);
    }

    public void RemoveSelectionObject(ComponentRemovedEvent<SelectableComponent> e)
    {
        if (_selectedEntityIds.Remove(e.EntityId))
            if (_selectionObjects.TryGetValue(e.EntityId, out var prefab))
                prefab.SetUnselected(_friendlyColor, _selectionColorProperty);

        DestroySelectionObject(e.EntityId);
    }

    public void DeleteSelectionObject(EntityDeletedEvent e)
    {
        _selectedEntityIds.Remove(e.EntityId);
        DestroySelectionObject(e.EntityId);
    }

    private void DestroySelectionObject(ulong entityId)
    {
        if (!_selectionObjects.TryGetValue(entityId, out var prefab)) return;
        Destroy(prefab.gameObject);
        _selectionObjects.Remove(entityId);
    }

    public void ApplySelectionPosition(ref PositionComponent pos, SelectionPrefab selection)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        selection.transform.position = new Vector3(pos.X, height + 1f, pos.Y);
    }
}
