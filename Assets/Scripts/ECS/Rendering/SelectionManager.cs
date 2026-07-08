using System.Collections.Generic;
using UnityEngine;

public class SelectionManager : Singleton<SelectionManager>
{
    private const float SingleSelectRadius = 2f;
    private const float DragThresholdPixels = 5f;

    [SerializeField] private GameObject _selectionPrefab;
    [SerializeField] private GameObject _targetingPrefab;
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
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<HealthComponent> _healthStore;
    private EntityChunkTracker _chunkTracker;

    private readonly Dictionary<ulong, SelectionPrefab> _selectionObjects = new();
    private readonly Dictionary<ulong, TargetingPrefab> _targetingObjects = new();
    private readonly HashSet<ulong> _selectedEntityIds = new();
    public IReadOnlyCollection<ulong> SelectedEntityIds => _selectedEntityIds;

    // Scratch maps (entityId -> effective TargetKind) reused each frame to diff which
    // entities are currently targeted by one of the local player's troops, so we only
    // call SetTargeted/SetUntargeted on entities whose state actually changed.
    private readonly Dictionary<ulong, TargetKind> _currentlyTargetedIds = new();
    private readonly Dictionary<ulong, TargetKind> _previouslyTargetedIds = new();

    // Left-drag (select) state
    private Vector2 _dragStartScreen;
    private float _dragStartTileX;
    private float _dragStartTileY;
    private bool _isDragging;

    // Right-drag (target/move) state
    private Vector2 _rightDragStartScreen;
    private float _rightDragStartTileX;
    private float _rightDragStartTileY;
    private bool _isRightDragging;

    private readonly List<ulong> _queryBuffer = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<TroopActivatedEvent>(SetupSelection);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<SelectableComponent>>(RemoveSelectionObject);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(DeleteSelectionObject);

        ECS ecs = TickManager.instance.ActiveECS;
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _selectableStore = ecs.GetComponentStore<SelectableComponent>();
        _troopStore = ecs.GetComponentStore<TroopComponent>();
        _healthStore = ecs.GetComponentStore<HealthComponent>();
        _chunkTracker = ecs.ChunkTracker;

        if (_dragSelectionBox != null)
            _dragSelectionBox.gameObject.SetActive(false);
    }

    public void Update()
    {
        // Guards against running before Initialize() has populated the component
        // stores / chunk tracker below (Initialize only runs once the game actually
        // starts, but Update fires every frame regardless).
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

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

        foreach (var kvp in _targetingObjects)
        {
            ulong entityId = kvp.Key;
            TargetingPrefab targeting = kvp.Value;

            if (_positionStore.HasComponent(entityId))
            {
                PositionComponent pos = _positionStore.GetComponent(entityId);
                ApplyTargetingPosition(ref pos, targeting);
            }
        }

        RefreshTargetingVisuals();
        HandleSelectionInput();
    }

    // Diffs which entities are currently targeted by one of the local player's troops
    // (and with what effective kind) against last frame's set, and toggles/recolors the
    // corresponding decal only on change. If different friendly troops target the same
    // entity with different kinds, PlayerAssigned wins for display purposes.
    private void RefreshTargetingVisuals()
    {
        TargetingSystem targeting = TickManager.instance.ActiveECS.GetSystem<TargetingSystem>();
        if (targeting == null) return;

        ushort localId = LocalPlayerId();
        _currentlyTargetedIds.Clear();

        _troopStore.ForEach((ulong friendlyId) => {
            if (_troopStore.GetComponent(friendlyId).OwnerPlayerId != localId) return;
            foreach (ulong targetId in targeting.GetTargets(friendlyId))
            {
                TargetKind kind = targeting.GetTargetKind(friendlyId, targetId) ?? TargetKind.Automatic;
                if (_currentlyTargetedIds.TryGetValue(targetId, out TargetKind existing) && existing == TargetKind.PlayerAssigned)
                    continue;
                _currentlyTargetedIds[targetId] = kind;
            }
        });

        foreach (KeyValuePair<ulong, TargetKind> kvp in _currentlyTargetedIds)
        {
            ulong entityId = kvp.Key;
            TargetKind kind = kvp.Value;
            if (_previouslyTargetedIds.TryGetValue(entityId, out TargetKind prevKind) && prevKind == kind) continue;
            if (_targetingObjects.TryGetValue(entityId, out TargetingPrefab prefab))
                prefab.SetTargeted(kind, _selectionColorProperty);
        }

        foreach (ulong entityId in _previouslyTargetedIds.Keys)
        {
            if (_currentlyTargetedIds.ContainsKey(entityId)) continue;
            if (_targetingObjects.TryGetValue(entityId, out TargetingPrefab prefab))
                prefab.SetUntargeted();
        }

        _previouslyTargetedIds.Clear();
        foreach (KeyValuePair<ulong, TargetKind> kvp in _currentlyTargetedIds)
            _previouslyTargetedIds[kvp.Key] = kvp.Value;
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
                UpdateDragBox(_dragStartScreen);
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

        HandleRightClickInput();
    }

    // Right click either sets targets (cursor is over a selectable enemy/neutral troop
    // with a HealthComponent) or issues a move command (anywhere else) for the
    // currently selected friendly troops. Held Space suppresses both, matching the
    // existing move-command convention. Held Shift makes a target command additive
    // instead of replacing the friendly troops' current targets.
    private void HandleRightClickInput()
    {
        if (Input.GetKey(KeyCode.Space)) return;

        if (Input.GetMouseButtonDown(1))
        {
            _rightDragStartScreen = Input.mousePosition;
            TileSpaceMouse.TryGetPosition(out _rightDragStartTileX, out _rightDragStartTileY);
            _isRightDragging = false;
        }

        if (Input.GetMouseButton(1))
        {
            Vector2 delta = (Vector2)Input.mousePosition - _rightDragStartScreen;
            if (!_isRightDragging && delta.magnitude > DragThresholdPixels)
            {
                _isRightDragging = true;
                if (_dragSelectionBox != null)
                    _dragSelectionBox.gameObject.SetActive(true);
            }

            if (_isRightDragging)
                UpdateDragBox(_rightDragStartScreen);
        }

        if (Input.GetMouseButtonUp(1))
        {
            if (_dragSelectionBox != null)
                _dragSelectionBox.gameObject.SetActive(false);

            if (_isRightDragging)
                PerformRectTargetOrMove();
            else
                PerformPointTargetOrMove();

            _isRightDragging = false;
        }
    }

    // ── Selection logic ───────────────────────────────────────────────────────

    private ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    private bool IsFriendly(ulong entityId)
    {
        if (!_selectableStore.HasComponent(entityId)) return false;
        if (_troopStore.HasComponent(entityId) && !_troopStore.GetComponent(entityId).CanTakeActions) return false;
        return _selectableStore.GetComponent(entityId).OwnerPlayerId == LocalPlayerId();
    }

    // Valid target for a right-click: selectable, has health, and not owned by the
    // local player (covers both enemy and neutral troops).
    private bool IsTargetable(ulong entityId)
    {
        if (!_selectableStore.HasComponent(entityId)) return false;
        if (_healthStore == null || !_healthStore.HasComponent(entityId)) return false;
        return _selectableStore.GetComponent(entityId).OwnerPlayerId != LocalPlayerId();
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

    private void PerformPointTargetOrMove()
    {
        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty)) return;

        ulong targetId = FindPointTarget(tx, ty);
        if (targetId != 0)
        {
            SendSetTargets(new List<ulong> { targetId });
            return;
        }

        // Nothing targetable under the cursor — clear the selection's current targets
        // (an empty, non-additive SetTargetsInput) before issuing the move.
        SendSetTargets(new List<ulong>());
        SendMoveCommand(tx, ty);
    }

    private ulong FindPointTarget(float tx, float ty)
    {
        _queryBuffer.Clear();
        _chunkTracker.GetEntitiesNear(tx, ty, SingleSelectRadius, _queryBuffer);

        ulong bestId = 0;
        float bestDist2 = SingleSelectRadius * SingleSelectRadius;

        foreach (ulong entityId in _queryBuffer)
        {
            if (!IsTargetable(entityId)) continue;
            PositionComponent pos = _positionStore.GetComponent(entityId);
            float dx = pos.X - tx, dy = pos.Y - ty;
            float d2 = dx * dx + dy * dy;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                bestId = entityId;
            }
        }

        return bestId;
    }

    private void PerformRectTargetOrMove()
    {
        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty)) return;

        float minX = Mathf.Min(_rightDragStartTileX, tx);
        float maxX = Mathf.Max(_rightDragStartTileX, tx);
        float minY = Mathf.Min(_rightDragStartTileY, ty);
        float maxY = Mathf.Max(_rightDragStartTileY, ty);

        _queryBuffer.Clear();
        _chunkTracker.GetEntitiesInRect(minX, minY, maxX, maxY, _queryBuffer);

        List<ulong> targets = new List<ulong>();
        foreach (ulong entityId in _queryBuffer)
            if (IsTargetable(entityId)) targets.Add(entityId);

        if (targets.Count > 0)
            SendSetTargets(targets);
    }

    private void SendSetTargets(List<ulong> targetIds)
    {
        if (_selectedEntityIds.Count == 0) return;

        bool additionalSelect = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        InputBuffer.EnqueueInput(new SetTargetsInput
        {
            FriendlyTroopIds = new List<ulong>(_selectedEntityIds),
            TargetTroopIds   = targetIds,
            AdditionalSelect = additionalSelect,
        });
    }

    private void SendMoveCommand(float tx, float ty)
    {
        if (_selectedEntityIds.Count == 0) return;

        List<MoveTroopInput.EntityDestination> moves = new List<MoveTroopInput.EntityDestination>();
        foreach (ulong entityId in _selectedEntityIds)
        {
            moves.Add(new MoveTroopInput.EntityDestination
            {
                EntityId     = entityId,
                DestinationX = tx,
                DestinationY = ty,
            });
        }

        InputBuffer.EnqueueInput(new MoveTroopInput { Moves = moves });
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

    private void UpdateDragBox(Vector2 startScreen)
    {
        if (_dragSelectionBox == null) return;
        Vector2 current = Input.mousePosition;
        Vector2 min = Vector2.Min(startScreen, current);
        Vector2 max = Vector2.Max(startScreen, current);
        // Force anchor and pivot to bottom-left so offsetMin/offsetMax map
        // directly to screen-pixel corners regardless of inspector settings.
        _dragSelectionBox.anchorMin = Vector2.zero;
        _dragSelectionBox.anchorMax = Vector2.zero;
        _dragSelectionBox.pivot = Vector2.zero;
        _dragSelectionBox.anchoredPosition = min;
        _dragSelectionBox.sizeDelta = max - min;
    }

    // ── Selection object lifecycle ────────────────────────────────────────────

    public void SetupSelection(TroopActivatedEvent e)
    {
        if (_selectionObjects.ContainsKey(e.EntityId)) return;
        if (!_selectableStore.HasComponent(e.EntityId)) return;
        if (!_positionStore.HasComponent(e.EntityId)) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        GameObject go = Instantiate(_selectionPrefab, transform);
        go.name = $"Selection_{e.EntityId}";
        SelectionPrefab prefab = go.GetComponent<SelectionPrefab>();
        _selectionObjects[e.EntityId] = prefab;
        ApplySelectionPosition(ref pos, prefab);
        prefab.SetUnselected(GetUnselectedColor(e.EntityId), _selectionColorProperty);

        if (_targetingPrefab != null)
        {
            GameObject targetingGo = Instantiate(_targetingPrefab, transform);
            targetingGo.name = $"Targeting_{e.EntityId}";
            TargetingPrefab targetingObj = targetingGo.GetComponent<TargetingPrefab>();
            _targetingObjects[e.EntityId] = targetingObj;
            ApplyTargetingPosition(ref pos, targetingObj);
        }
    }

    public void RemoveSelectionObject(ComponentRemovedEvent<SelectableComponent> e)
    {
        if (_selectedEntityIds.Remove(e.EntityId))
            if (_selectionObjects.TryGetValue(e.EntityId, out var prefab))
                prefab.SetUnselected(_friendlyColor, _selectionColorProperty);

        DestroySelectionObject(e.EntityId);
        DestroyTargetingObject(e.EntityId);
    }

    public void DeleteSelectionObject(EntityDeletedEvent e)
    {
        _selectedEntityIds.Remove(e.EntityId);
        DestroySelectionObject(e.EntityId);
        DestroyTargetingObject(e.EntityId);
    }

    private void DestroySelectionObject(ulong entityId)
    {
        if (!_selectionObjects.TryGetValue(entityId, out var prefab)) return;
        Destroy(prefab.gameObject);
        _selectionObjects.Remove(entityId);
    }

    private void DestroyTargetingObject(ulong entityId)
    {
        if (!_targetingObjects.TryGetValue(entityId, out var prefab)) return;
        Destroy(prefab.gameObject);
        _targetingObjects.Remove(entityId);
    }

    public void ApplySelectionPosition(ref PositionComponent pos, SelectionPrefab selection)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        selection.transform.position = new Vector3(pos.X, height + 1f, pos.Y);
    }

    public void ApplyTargetingPosition(ref PositionComponent pos, TargetingPrefab targeting)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        targeting.transform.position = new Vector3(pos.X, height + 1f, pos.Y);
    }
}
