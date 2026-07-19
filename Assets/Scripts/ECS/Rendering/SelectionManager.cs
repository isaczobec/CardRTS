using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class SelectionManager : Singleton<SelectionManager>
{
    private const float SingleSelectRadius = 2.2f;
    // Deliberately generous: a plain click that twitches a few pixels while releasing
    // the mouse button should never be misread as the start of a drag-select.
    private const float DragThresholdPixels = 130f;
    // How far from the cursor the A-key nearest-target hotkey (HandleTargetHotkeyInput)
    // will reach to find an enemy/neutral troop — deliberately much more generous than
    // SingleSelectRadius, since there's no visual "am I close enough" feedback for a
    // keypress the way there is for clicking directly on/near a troop.
    private const float TargetHotkeyRadius = 40f;

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


    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<SelectableComponent> _selectableStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<HealthComponent> _healthStore;
    private ComponentStore<MovableComponent> _movableStore;
    private EntityChunkTracker _chunkTracker;

    private readonly Dictionary<ulong, SelectionPrefab> _selectionObjects = new();
    private readonly Dictionary<ulong, TargetingPrefab> _targetingObjects = new();
    private readonly TickPositionInterpolator _interpolator = new();
    private readonly HashSet<ulong> _selectedEntityIds = new();
    public IReadOnlyCollection<ulong> SelectedEntityIds => _selectedEntityIds;

    // Most recently added-to-selection entity still actually in _selectedEntityIds — see
    // TryGetFocusPositionForSelection (CameraController's Space hotkey).
    private ulong _lastSelectedEntityId;

    // Last entity CameraController's Tab hotkey jumped to — see TryGetNextOwnedEntityPosition.
    private ulong _lastCycledOwnedEntityId;
    private readonly List<ulong> _ownedEntityCycleBuffer = new();

    // True while a selection or targeting drag box is actively being dragged. CameraController
    // checks this to suppress edge-scroll — otherwise dragging a box near the screen edge
    // would also pan the camera out from under you.
    public bool IsDragActive => _isDragging || _isRightDragging;

    // Scratch maps (entityId -> effective TargetKind) reused each frame to diff which
    // entities are currently targeted by one of the local player's troops, so we only
    // call SetTargeted/SetUntargeted on entities whose state actually changed.
    private readonly Dictionary<ulong, TargetKind> _currentlyTargetedIds = new();
    private readonly Dictionary<ulong, TargetKind> _previouslyTargetedIds = new();

    // Left-drag (select) state
    private Vector2 _dragStartScreen;
    private bool _isDragging;
    // Set on left-mouse-down, from whether the pointer was over a UI element (e.g. the
    // minimap) at that moment — a click/drag that started on UI should never fall through
    // to world point/rect select (which would otherwise deselect the current selection).
    private bool _leftDownOverUI;

    // Set by CardHandRenderer.PlayCard right when a click or drag-release plays a card
    // (see SuppressNextClickSelect) — consumed by the very next GetMouseButtonUp(0), then
    // cleared. Needed because that same click/release is what CardHandRenderer used to
    // clear its own "card selected/dragging" state — by the time this class's mouse-up
    // handling runs, IsCardSelectedOrDragging/IsCardHovered are already back to false, so
    // they can no longer distinguish "a card was just played here" from "nothing was ever
    // selected." This isn't a script-execution-order thing to work around: the click that
    // plays a card (GetMouseButtonDown) and the release that would otherwise deselect
    // troops (GetMouseButtonUp) are two different events, potentially different frames —
    // the card's selected state is genuinely gone in between, not just racily read.
    private bool _suppressNextClickSelect;

    // Called by CardHandRenderer.PlayCard for both the click-to-play and drag-to-play
    // paths, right before enqueuing the play input.
    public void SuppressNextClickSelect() => _suppressNextClickSelect = true;

    // Right-drag (target/move) state
    private Vector2 _rightDragStartScreen;
    private bool _isRightDragging;
    // Same idea as _leftDownOverUI — a right-click that started on UI (e.g. the minimap,
    // which issues its own target/move command directly) shouldn't also resolve a ground
    // raycast from wherever the cursor happens to be.
    private bool _rightDownOverUI;

    private readonly List<ulong> _queryBuffer = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(SetupSelection);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<SelectableComponent>>(RemoveSelectionObject);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(DeleteSelectionObject);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityDiedEvent>(OnRespawnableEntityDied);
        TickManager.instance.ServerFlagEvents.Subscribe<RespawnableEntityRespawnedEvent>(OnRespawnableEntityRespawned);

        _ecs = TickManager.instance.ActiveECS;
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _selectableStore = _ecs.GetComponentStore<SelectableComponent>();
        _troopStore = _ecs.GetComponentStore<TroopComponent>();
        _healthStore = _ecs.GetComponentStore<HealthComponent>();
        _movableStore = _ecs.GetComponentStore<MovableComponent>();
        _chunkTracker = _ecs.ChunkTracker;

        // Veto selectability for any entity that cannot currently take actions
        // (not yet activated, or dead). Complements the RespawnSystem subscriber
        // which also vetoes entities currently on a respawn cooldown.
        _ecs.Requests.Subscribe<IsSelectableRequest>((req, ecs) =>
        {
            if (!ActivationQuery.CanTakeActions(ecs, req.EntityId))
                req.IsSelectable = false;
        });

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
                ApplySelectionPosition(entityId, ref pos, selection);
            }
        }

        foreach (var kvp in _targetingObjects)
        {
            ulong entityId = kvp.Key;
            TargetingPrefab targeting = kvp.Value;

            if (_positionStore.HasComponent(entityId))
            {
                PositionComponent pos = _positionStore.GetComponent(entityId);
                ApplyTargetingPosition(entityId, ref pos, targeting);
            }
        }

        RefreshTargetingVisuals();
        HandleSelectionInput();
        HandleTargetHotkeyInput();
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
                if (!IsEntitySelectable(targetId)) continue;
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
                prefab.SetTargeted(kind);
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

    // Held Left Alt is reserved for camera pan/rotate (see CameraController) — while it's
    // down, no selection box may appear and no selection (drag or point) can be made.
    private void HandleSelectionInput()
    {
        if (Input.GetKey(KeyCode.LeftAlt))
        {
            // Cancel any drag that was already in progress before Alt was pressed, rather
            // than leaving it to resolve once Alt is released.
            if (_isDragging && _dragSelectionBox != null)
                _dragSelectionBox.gameObject.SetActive(false);
            _isDragging = false;
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
            {
                _dragStartScreen = Input.mousePosition;
                _isDragging = false;
                // A click that starts over UI (e.g. the minimap), or while a card is
                // selected/being dragged/hovered (CardHandRenderer) — where a click means
                // "play the card here" (or is just passing over its hand icon), never
                // "select/target troops" — must never fall through to PerformPointSelect on
                // release. Latched here at mouse-DOWN rather than re-checked live at
                // mouse-up: a drag already in progress must always finish (hide its box,
                // reset _isDragging) on release regardless of what the cursor drifts over
                // in between — e.g. releasing a drag-select over the hand while it happens
                // to be hovering a card must still clear the box and select, not silently
                // do nothing because IsCardHovered is true *now*.
                bool cardActive = CardHandRenderer.instance != null &&
                    (CardHandRenderer.instance.IsCardSelectedOrDragging || CardHandRenderer.instance.IsCardHovered);
                _leftDownOverUI = cardActive || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
            }

            if (Input.GetMouseButton(0) && !_leftDownOverUI)
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

                if (_suppressNextClickSelect)
                {
                    // See SuppressNextClickSelect: this release belongs to a click/drag
                    // that CardHandRenderer already consumed to play a card.
                    _suppressNextClickSelect = false;
                }
                // A click/drag that started over UI (e.g. the minimap) never reaches world
                // point/rect select — otherwise clicking the minimap to jump the camera
                // would also deselect whatever was currently selected.
                else if (!_leftDownOverUI)
                {
                    if (_isDragging)
                        PerformRectSelect();
                    else
                        PerformPointSelect();
                }

                _isDragging = false;
            }
        }

        HandleRightClickInput();
    }

    // Pressing A targets the closest enemy/neutral troop to the cursor, within
    // TargetHotkeyRadius, for the currently selected friendly troops — same effect as
    // right-clicking directly on that troop (SendSetTargets), just without needing the
    // cursor to be right on top of it. No-ops with nothing selected, the console open, or
    // Left Alt held (camera pan), matching every other selection input's own guards.
    private void HandleTargetHotkeyInput()
    {
        if (DevConsole.IsOpen) return;
        if (Input.GetKey(KeyCode.LeftAlt)) return;
        if (!Input.GetKeyDown(KeyCode.A)) return;
        if (_selectedEntityIds.Count == 0) return;

        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty)) return;

        ulong targetId = FindClosestTargetable(tx, ty, TargetHotkeyRadius);
        if (targetId == 0) return;

        SendSetTargets(new List<ulong> { targetId });
    }

    // Right click either sets targets (cursor is over a selectable enemy/neutral troop
    // with a HealthComponent) or issues a move command (anywhere else) for the
    // currently selected friendly troops. Held Left Alt suppresses both, matching the
    // existing move-command convention. Held Shift makes a target command additive
    // instead of replacing the friendly troops' current targets.
    private void HandleRightClickInput()
    {
        if (Input.GetKey(KeyCode.LeftAlt))
        {
            // Cancel any drag that was already in progress before Alt was pressed, rather
            // than leaving the box stuck on screen until Alt is released.
            if (_isRightDragging && _dragSelectionBox != null)
                _dragSelectionBox.gameObject.SetActive(false);
            _isRightDragging = false;
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            _rightDragStartScreen = Input.mousePosition;
            _isRightDragging = false;
            // Same latch-at-mouse-down reasoning as _leftDownOverUI in HandleSelectionInput.
            bool cardActive = CardHandRenderer.instance != null &&
                (CardHandRenderer.instance.IsCardSelectedOrDragging || CardHandRenderer.instance.IsCardHovered);
            _rightDownOverUI = cardActive || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        }

        if (Input.GetMouseButton(1) && !_rightDownOverUI)
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

            // A right-click that started over UI (e.g. the minimap, which issues its own
            // target/move command) never also resolves a ground raycast from here.
            if (!_rightDownOverUI)
            {
                if (_isRightDragging)
                    PerformRectTargetOrMove();
                else
                    PerformPointTargetOrMove();
            }

            _isRightDragging = false;
        }
    }

    // ── Selection logic ───────────────────────────────────────────────────────

    private ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    private static bool IsAdditiveModifierHeld()
        => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    private bool IsEntitySelectable(ulong entityId)
        => _ecs != null && _ecs.Requests.Process(new IsSelectableRequest(entityId), _ecs, executeIfNotCancelled: false).IsSelectable;

    private bool IsFriendly(ulong entityId)
    {
        if (!_selectableStore.HasComponent(entityId)) return false;
        if (!IsEntitySelectable(entityId)) return false;
        return _selectableStore.GetComponent(entityId).OwnerPlayerId == LocalPlayerId();
    }

    // Valid target for a right-click: selectable, has health, and not owned by the
    // local player (covers both enemy and neutral troops).
    private bool IsTargetable(ulong entityId)
    {
        if (!_selectableStore.HasComponent(entityId)) return false;
        if (_healthStore == null || !_healthStore.HasComponent(entityId)) return false;
        if (!IsEntitySelectable(entityId)) return false;
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
        bool additive = IsAdditiveModifierHeld();

        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty))
        {
            if (!additive) DeselectAll();
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

        if (!additive) DeselectAll();
        if (bestId != 0)
            Select(bestId, additive ? _friendlySelectedColor : _friendlySingleSelectedColor);
    }

    private void PerformRectSelect()
    {
        _queryBuffer.Clear();
        GetEntitiesInScreenRect(_dragStartScreen, Input.mousePosition, IsFriendly, _queryBuffer);

        if (!IsAdditiveModifierHeld()) DeselectAll();
        foreach (ulong entityId in _queryBuffer)
            Select(entityId, _friendlySelectedColor);
    }

    // Screen-space containment test: projects each candidate's rendered world position
    // through the camera and checks it against the on-screen drag rectangle. The old
    // approach built an axis-aligned world-space rect from just the two ground-ray-hit
    // corner points, which only matches what's visually enclosed when the camera looks
    // straight down the world axes — for any rotated/angled camera the on-screen
    // rectangle corresponds to a general quadrilateral in world space, not an axis-aligned
    // box, so troops visually inside the drag box could be missed (or outside ones caught).
    private void GetEntitiesInScreenRect(Vector2 screenStart, Vector2 screenEnd, System.Func<ulong, bool> filter, List<ulong> results)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 min = Vector2.Min(screenStart, screenEnd);
        Vector2 max = Vector2.Max(screenStart, screenEnd);

        _selectableStore.ForEach((ulong entityId) =>
        {
            if (!filter(entityId)) return;
            if (!_positionStore.HasComponent(entityId)) return;

            Vector3 screenPos = cam.WorldToScreenPoint(WorldPositionFor(_positionStore.GetComponent(entityId)));
            if (screenPos.z <= 0f) return; // behind the camera

            if (screenPos.x >= min.x && screenPos.x <= max.x && screenPos.y >= min.y && screenPos.y <= max.y)
                results.Add(entityId);
        });
    }

    private void PerformPointTargetOrMove()
    {
        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty)) return;
        PerformPointTargetOrMove(tx, ty);
    }

    // Exposed so other input sources — e.g. MinimapManager's right-click, which resolves
    // its own world position from a click on the minimap instead of a ground raycast — can
    // issue the exact same "target if something's there, else move" command.
    public void PerformPointTargetOrMove(float tx, float ty)
    {
        ulong targetId = FindClosestTargetable(tx, ty, SingleSelectRadius);
        if (targetId != 0)
        {
            SendSetTargets(new List<ulong> { targetId });
            return;
        }

        // Nothing targetable at the point — clear the selection's current targets (an
        // empty, non-additive SetTargetsInput) before issuing the move.
        SendSetTargets(new List<ulong>());
        SendMoveCommand(tx, ty);
    }

    // Closest IsTargetable entity to (tx, ty) within radius, or 0 if none. Shared by
    // right-click/point targeting (SingleSelectRadius) and the A-key nearest-target
    // hotkey (TargetHotkeyRadius) — same "closest valid target near a point" query, just a
    // different radius and trigger.
    private ulong FindClosestTargetable(float tx, float ty, float radius)
    {
        _queryBuffer.Clear();
        _chunkTracker.GetEntitiesNear(tx, ty, radius, _queryBuffer);

        ulong bestId = 0;
        float bestDist2 = radius * radius;

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
        _queryBuffer.Clear();
        GetEntitiesInScreenRect(_rightDragStartScreen, Input.mousePosition, IsTargetable, _queryBuffer);

        // SendSetTargets stores this list by reference in a queued SetTargetsInput that
        // can outlive this call by several frames — must not hand it the reused scratch
        // buffer, which gets cleared again on the next click.
        if (_queryBuffer.Count > 0)
            SendSetTargets(new List<ulong>(_queryBuffer));
    }

    private void SendSetTargets(List<ulong> targetIds)
    {
        if (_selectedEntityIds.Count == 0) return;

        bool additionalSelect = IsAdditiveModifierHeld();

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
        _lastSelectedEntityId = entityId;
        if (_selectionObjects.TryGetValue(entityId, out var prefab))
            prefab.SetSelected(color);
    }

    private void DeselectAll()
    {
        foreach (ulong entityId in _selectedEntityIds)
            if (_selectionObjects.TryGetValue(entityId, out var prefab))
                prefab.SetUnselected(GetUnselectedColor(entityId));
        _selectedEntityIds.Clear();
        _lastSelectedEntityId = 0;
    }

    // ── Camera hotkeys (Tab/Space — see CameraController) ───────────────────────

    // Space (held): continuously follows the current selection — the one selected entity's
    // position if exactly one is selected, or the most recently selected one's position if
    // there are several. False (no follow) if nothing is selected.
    //
    // Routed through _interpolator (the same one ApplySelectionPosition uses for this same
    // entity's own selection ring) rather than the entity's raw PositionComponent, which
    // only changes once per simulation tick — reusing it means the camera glides smoothly
    // frame-to-frame in step with the ring instead of visibly stepping once per tick.
    public bool TryGetFocusPositionForSelection(out Vector3 worldPos)
    {
        worldPos = default;
        if (_selectedEntityIds.Count == 0 || _positionStore == null) return false;

        ulong targetId = (_lastSelectedEntityId != 0 && _selectedEntityIds.Contains(_lastSelectedEntityId))
            ? _lastSelectedEntityId
            : FirstSelectedId();

        if (targetId == 0 || !_positionStore.HasComponent(targetId)) return false;

        Vector3 rawWorldPos = WorldPositionFor(_positionStore.GetComponent(targetId));
        worldPos = _interpolator.Update(targetId, rawWorldPos, IsMoving(targetId), IsTeleported(targetId));
        return true;
    }

    private ulong FirstSelectedId()
    {
        foreach (ulong id in _selectedEntityIds) return id;
        return 0;
    }

    // Tab: cycles the camera through every entity the local player owns (independent of
    // the current selection), advancing to the next one each call and wrapping back to the
    // first once the last is reached. Ordered by ascending entity id, which is stable as
    // long as the set of owned entities doesn't change between calls.
    public bool TryGetNextOwnedEntityPosition(out Vector3 worldPos)
    {
        worldPos = default;
        if (_selectableStore == null || _positionStore == null) return false;

        _ownedEntityCycleBuffer.Clear();
        _selectableStore.ForEach((ulong entityId) =>
        {
            if (IsFriendly(entityId))
                _ownedEntityCycleBuffer.Add(entityId);
        });

        if (_ownedEntityCycleBuffer.Count == 0) return false;
        _ownedEntityCycleBuffer.Sort();

        int nextIndex = 0;
        int lastIndex = _ownedEntityCycleBuffer.IndexOf(_lastCycledOwnedEntityId);
        if (lastIndex >= 0)
            nextIndex = (lastIndex + 1) % _ownedEntityCycleBuffer.Count;

        ulong nextId = _ownedEntityCycleBuffer[nextIndex];
        _lastCycledOwnedEntityId = nextId;

        if (!_positionStore.HasComponent(nextId)) return false;
        worldPos = WorldPositionFor(_positionStore.GetComponent(nextId));
        return true;
    }

    // ── Drag box ─────────────────────────────────────────────────────────────

    private void UpdateDragBox(Vector2 startScreen)
    {
        if (_dragSelectionBox == null) return;

        // Input.mousePosition is always in real screen pixels, but anchoredPosition/
        // sizeDelta are in the parent's local UI units — those only match 1:1 when the
        // Canvas's scale factor happens to be 1. With a CanvasScaler in "Scale With
        // Screen Size" mode (as this scene uses) that's only true at the reference
        // resolution, so writing raw screen pixels straight into anchoredPosition drifts
        // the box away from the cursor at any other resolution. Go through
        // ScreenPointToLocalPointInRectangle so this holds for any scaler/render mode.
        RectTransform parent = _dragSelectionBox.parent as RectTransform;
        if (parent == null) return;

        Canvas canvas = _dragSelectionBox.GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, startScreen, cam, out Vector2 startLocal) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, cam, out Vector2 currentLocal))
            return;

        // ScreenPointToLocalPointInRectangle returns points relative to the parent's own
        // pivot; shift by its min corner so they land in the same space as the box's
        // anchoredPosition below, which is measured from the parent's bottom-left corner
        // (anchorMin/anchorMax/pivot are forced to (0,0) here regardless of inspector
        // settings).
        Vector2 offset = parent.rect.min;
        Vector2 a = startLocal - offset;
        Vector2 b = currentLocal - offset;
        Vector2 min = Vector2.Min(a, b);
        Vector2 max = Vector2.Max(a, b);

        _dragSelectionBox.anchorMin = Vector2.zero;
        _dragSelectionBox.anchorMax = Vector2.zero;
        _dragSelectionBox.pivot = Vector2.zero;
        _dragSelectionBox.anchoredPosition = min;
        _dragSelectionBox.sizeDelta = max - min;
    }

    // ── Selection object lifecycle ────────────────────────────────────────────

    private void OnRespawnableEntityDied(RespawnableEntityDiedEvent e)
    {
        _selectedEntityIds.Remove(e.EntityId);
        if (_selectionObjects.TryGetValue(e.EntityId, out SelectionPrefab prefab))
            prefab.gameObject.SetActive(false);
    }

    private void OnRespawnableEntityRespawned(RespawnableEntityRespawnedEvent e)
    {
        if (_selectionObjects.TryGetValue(e.EntityId, out SelectionPrefab prefab))
            prefab.gameObject.SetActive(true);
    }

    public void SetupSelection(EntityActivatedEvent e)
    {
        if (_selectionObjects.ContainsKey(e.EntityId)) return;
        if (!_selectableStore.HasComponent(e.EntityId)) return;
        if (!_positionStore.HasComponent(e.EntityId)) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        // 0 means the spawning code never set SelectableComponent.Scale — fall back to 1
        // rather than shrinking the ring to nothing.
        float scale = _selectableStore.GetComponent(e.EntityId).Scale;
        if (scale <= 0f) scale = 1f;

        GameObject go = Instantiate(_selectionPrefab, transform);
        go.name = $"Selection_{e.EntityId}";
        SelectionPrefab prefab = go.GetComponent<SelectionPrefab>();
        _selectionObjects[e.EntityId] = prefab;
        prefab.SetScale(scale);
        ApplySelectionPosition(e.EntityId, ref pos, prefab);
        prefab.SetUnselected(GetUnselectedColor(e.EntityId));

        if (_targetingPrefab != null)
        {
            GameObject targetingGo = Instantiate(_targetingPrefab, transform);
            targetingGo.name = $"Targeting_{e.EntityId}";
            TargetingPrefab targetingObj = targetingGo.GetComponent<TargetingPrefab>();
            _targetingObjects[e.EntityId] = targetingObj;
            targetingObj.SetScale(scale);
            ApplyTargetingPosition(e.EntityId, ref pos, targetingObj);
        }

        // An entity can already be dead the moment it activates (e.g. a world-gen resource
        // node spawned dead-on-spawn — see EntitySpawnAction.SpawnSoulstoneNode) — that never
        // raises RespawnableEntityDiedEvent (nothing ever transitioned from alive to dead), so
        // the ring defaulting to visible above needs correcting for that case right here,
        // mirroring what OnRespawnableEntityDied does for an entity that dies later.
        if (_troopStore != null && _troopStore.HasComponent(e.EntityId) && _troopStore.GetComponent(e.EntityId).IsDead)
            prefab.gameObject.SetActive(false);
    }

    public void RemoveSelectionObject(ComponentRemovedEvent<SelectableComponent> e)
    {
        if (_selectedEntityIds.Remove(e.EntityId))
            if (_selectionObjects.TryGetValue(e.EntityId, out var prefab))
                prefab.SetUnselected(_friendlyColor);

        DestroySelectionObject(e.EntityId);
        DestroyTargetingObject(e.EntityId);
        _interpolator.Remove(e.EntityId);
    }

    public void DeleteSelectionObject(EntityDeletedEvent e)
    {
        _selectedEntityIds.Remove(e.EntityId);
        DestroySelectionObject(e.EntityId);
        DestroyTargetingObject(e.EntityId);
        _interpolator.Remove(e.EntityId);
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

    public void ApplySelectionPosition(ulong entityId, ref PositionComponent pos, SelectionPrefab selection)
    {
        Vector3 worldPos = WorldPositionFor(pos);
        selection.transform.position = _interpolator.Update(entityId, worldPos, IsMoving(entityId), IsTeleported(entityId));
    }

    public void ApplyTargetingPosition(ulong entityId, ref PositionComponent pos, TargetingPrefab targeting)
    {
        Vector3 worldPos = WorldPositionFor(pos);
        targeting.transform.position = _interpolator.Update(entityId, worldPos, IsMoving(entityId), IsTeleported(entityId));
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }

    private bool IsMoving(ulong entityId)
        => _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).currentMovementMode != MovementMode.NotMoving;

    private bool IsTeleported(ulong entityId)
        => _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).TeleportedTick == _ecs.CurrentSimulationTick;
}
