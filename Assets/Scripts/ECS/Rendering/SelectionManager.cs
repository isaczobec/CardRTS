using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class SelectionManager : Singleton<SelectionManager>
{
    private const float SingleSelectRadius = 2.2f;

    // Fallback for StatsQuery.GetSpeed below, mirrors BasicTroopRenderer's own DefaultSpeed.
    private const int DefaultSpeed = 100;
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
    // Separate instances, even though a given entity's selection ring and targeting ring
    // always track the identical underlying position — TickPositionInterpolator.Update
    // mutates its own per-entity Sample as a side effect (advancing the lenient overload's
    // MoveTowards chase by Time.deltaTime), so calling it twice in the same frame for the
    // same entityId (once per ring) through a SHARED instance would advance that chase
    // TWICE in one frame, converging up to 2x faster than BasicTroopRenderer's own (single-
    // call) interpolator — which is exactly what made the ring visibly run ahead of the
    // model. Two independent instances each get called exactly once per frame, so each
    // chases the true position at the correct rate and both stay in step with the model.
    private readonly TickPositionInterpolator _interpolator = new();
    private readonly TickPositionInterpolator _targetingInterpolator = new();
    private readonly HashSet<ulong> _selectedEntityIds = new();
    public IReadOnlyCollection<ulong> SelectedEntityIds => _selectedEntityIds;

    // Raised once per explicit move order the LOCAL player issues for their own selection
    // (right-click/minimap-click on empty ground — see SendMoveCommand, its only raise site),
    // with the world/tile-space point actually clicked (not each individual troop's own
    // formation-offset destination) and a snapshot (safe to hold onto — NOT a live reference
    // to _selectedEntityIds, which keeps changing) of every entity the order was issued to —
    // MoveMarkerManager subscribes to this to spawn a marker there and keep it alive until
    // that move order is actually completed/cancelled. Never raised for a target command
    // (right-clicking an entity) or for another player's moves, which this client has no
    // direct visibility into anyway.
    public event Action<Vector2, List<ulong>> MoveCommandIssued;

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

    // Scratch set, rebuilt each frame alongside _currentlyTargetedIds: every currently-
    // targeted entity that at least one of ITS targeters is currently selected — see
    // TargetingPrefab.SetTargeterSelected. Independent of TargetKind/color, so this is
    // (re)applied every frame regardless of whether _currentlyTargetedIds itself changed.
    private readonly HashSet<ulong> _targetedBySelectedTroop = new();

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
            if (!ActivationQuery.IsActivated(ecs, req.EntityId))
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

            // Polled every frame (rather than event-driven, the way OnRespawnableEntityDied/
            // Respawned toggle visibility for the dead-awaiting-respawn case) since there's
            // no single event fired uniformly for every way an untargetable state can end —
            // a Shadow Cloak modifier can expire naturally OR be broken instantly by
            // StalkerCard's ambush payoff. Harmless overlap with the respawn-event-driven
            // toggling above: IsEntitySelectable already folds RespawnSystem's own
            // (viewer-agnostic) veto in too, so both paths always agree on that case and
            // this is just a redundant re-application of the same value, not a fight.
            selection.gameObject.SetActive(IsEntitySelectable(entityId));
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
        HandleSelectAllTroopsInput();
        HandleSelectVisibleTroopsInput();
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
        _targetedBySelectedTroop.Clear();

        _troopStore.ForEach((ulong friendlyId) => {
            if (_troopStore.GetComponent(friendlyId).OwnerPlayerId != localId) return;
            bool friendlySelected = _selectedEntityIds.Contains(friendlyId);
            foreach (ulong targetId in targeting.GetTargets(friendlyId))
            {
                if (!IsEntitySelectable(targetId)) continue;
                if (friendlySelected) _targetedBySelectedTroop.Add(targetId);

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
            if (!_targetingObjects.TryGetValue(entityId, out TargetingPrefab prefab)) continue;

            if (!_previouslyTargetedIds.TryGetValue(entityId, out TargetKind prevKind) || prevKind != kind)
                prefab.SetTargeted(kind);

            // Independent of TargetKind/color (a targeter being selected/deselected doesn't
            // touch _currentlyTargetedIds at all), so this is reapplied every frame
            // regardless — cheap, SetTargeterSelected itself no-ops when unchanged.
            prefab.SetTargeterSelected(_targetedBySelectedTroop.Contains(entityId));
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
    // cursor to be right on top of it. No-ops with nothing selected, the console open, Left
    // Alt held (camera pan), or Ctrl held (Ctrl+A is the separate "select all friendly
    // troops" hotkey instead — see HandleSelectAllTroopsInput; without this guard, holding
    // Ctrl while pressing A would fire BOTH, since GetKeyDown(KeyCode.A) doesn't care about
    // modifier keys on its own).
    private void HandleTargetHotkeyInput()
    {
        if (DevConsole.IsOpen) return;
        if (Input.GetKey(KeyCode.LeftAlt)) return;
        if (IsCtrlHeld()) return;
        if (!Input.GetKeyDown(KeyCode.A)) return;
        if (_selectedEntityIds.Count == 0) return;

        if (!TileSpaceMouse.TryGetPosition(out float tx, out float ty)) return;

        ulong targetId = FindClosestTargetable(tx, ty, TargetHotkeyRadius);
        if (targetId == 0) return;

        SendSetTargets(new List<ulong> { targetId });
    }

    // Ctrl+A: selects every friendly PHYSICAL troop the local player owns (TroopComponent.
    // IsPhysicalTroop — false for every building, including a CapturableBuildingComponent
    // "spawn crystal" once captured, see BuildingSpawnHelper.AddBuildingComponents) anywhere
    // on the map, replacing whatever was previously selected — explicit design ask ("CTRL+A
    // selects all friendly troops (not buildings)"). Mirrors TryGetNextOwnedEntityPosition's
    // own IsFriendly-filtered ForEach scan over the whole map, just also gated on troop-ness
    // and actually selecting instead of cycling the camera.
    private void HandleSelectAllTroopsInput()
    {
        if (DevConsole.IsOpen) return;
        if (!IsCtrlHeld()) return;
        if (!Input.GetKeyDown(KeyCode.A)) return;
        if (_troopStore == null || _selectableStore == null) return;

        DeselectAll();

        _selectableStore.ForEach((ulong entityId) =>
        {
            if (IsFriendlyPhysicalTroop(entityId))
                Select(entityId, _friendlySelectedColor);
        });
    }

    // Ctrl+S: same as Ctrl+A, but scoped to whatever's currently visible in the camera's
    // viewport instead of the whole map — explicit design ask ("CTRL+S make all troops that
    // visible from the camera selected"). Reuses GetEntitiesInScreenRect's own screen-space
    // projection (see PerformRectSelect), just spanning the full screen instead of a drag
    // rectangle, filtered by the same friendly-physical-troop predicate Ctrl+A uses.
    private void HandleSelectVisibleTroopsInput()
    {
        if (DevConsole.IsOpen) return;
        if (!IsCtrlHeld()) return;
        if (!Input.GetKeyDown(KeyCode.S)) return;
        if (_troopStore == null || _selectableStore == null) return;

        _queryBuffer.Clear();
        GetEntitiesInScreenRect(Vector2.zero, new Vector2(Screen.width, Screen.height), IsFriendlyPhysicalTroop, _queryBuffer);

        DeselectAll();
        foreach (ulong entityId in _queryBuffer)
            Select(entityId, _friendlySelectedColor);
    }

    // Shared by both Ctrl+A and Ctrl+S — a real, physical troop (not a building — see
    // TroopComponent.IsPhysicalTroop's own doc comment) owned by the local player.
    private bool IsFriendlyPhysicalTroop(ulong entityId)
        => IsFriendly(entityId) && _troopStore.HasComponent(entityId) && _troopStore.GetComponent(entityId).IsPhysicalTroop;

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

    private static bool IsCtrlHeld()
        => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    private bool IsEntitySelectable(ulong entityId)
        => _ecs != null && _ecs.Requests.Process(new IsSelectableRequest(entityId, LocalPlayerId()), _ecs, executeIfNotCancelled: false).IsSelectable;

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

    // How far (world/tile units) a selected troop's position may be from the group's
    // centroid for its offset from it to still be preserved when a move command is issued.
    // Beyond this it's treated as a straggler, not really part of the same cluster as the
    // rest of the selection, and is just sent straight to the click point instead.
    private const float MaxFormationOffsetFromCentroid = 15f;

    // How far (tiles) SnapToWalkable will expand its search for the nearest walkable ground
    // before giving up on an unwalkable click/formation point — see that method.
    private const int SnapToWalkableSearchRadius = 24;

    // Sends each selected troop to its own destination, offset from the click point by the
    // same offset it currently has from the selection's centroid — so a multi-troop move
    // command preserves the group's relative formation instead of collapsing everyone onto
    // the exact same point. A single selected troop (the overwhelmingly common case) always
    // just goes straight to (tx, ty), same as before.
    private void SendMoveCommand(float tx, float ty)
    {
        if (_selectedEntityIds.Count == 0) return;

        Vector2 centroid = ComputeSelectionCentroid();

        // Snap the click itself onto walkable ground before it's used as anyone's
        // destination or shown as a marker — a raw click past the edge of an island (into
        // water/void) would otherwise send a troop at a point PathfindingSystem can only
        // ever reject outright, while MoveMarkerManager had already shown a marker there
        // with nothing to ever resolve it. This only ever adjusts a destination that isn't
        // walkable AT ALL; see SnapToWalkable's own doc comment for why it must never be
        // used to "walk around" an obstacle that merely sits between the troop and an
        // otherwise perfectly reachable destination — that's Pathfinding.PathFindNavMesh's
        // job, once it's actually handed the real click point.
        Vector2 clickPoint = SnapToWalkable(new Vector2(tx, ty));

        List<MoveTroopInput.EntityDestination> moves = new List<MoveTroopInput.EntityDestination>();
        foreach (ulong entityId in _selectedEntityIds)
        {
            Vector2 destination = clickPoint;

            if (_selectedEntityIds.Count > 1 && _positionStore.HasComponent(entityId))
            {
                PositionComponent pos = _positionStore.GetComponent(entityId);
                Vector2 offset = new Vector2(pos.X, pos.Y) - centroid;

                if (offset.sqrMagnitude <= MaxFormationOffsetFromCentroid * MaxFormationOffsetFromCentroid)
                    destination = SnapToWalkable(clickPoint + offset);
            }

            moves.Add(new MoveTroopInput.EntityDestination
            {
                EntityId     = entityId,
                DestinationX = destination.x,
                DestinationY = destination.y,
            });
        }

        InputBuffer.EnqueueInput(new MoveTroopInput { Moves = moves });
        MoveCommandIssued?.Invoke(clickPoint, new List<ulong>(_selectedEntityIds));
    }

    private Vector2 ComputeSelectionCentroid()
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        foreach (ulong entityId in _selectedEntityIds)
        {
            if (!_positionStore.HasComponent(entityId)) continue;
            PositionComponent pos = _positionStore.GetComponent(entityId);
            sum += new Vector2(pos.X, pos.Y);
            count++;
        }
        return count > 0 ? sum / count : Vector2.zero;
    }

    // Returns point unchanged if it's already walkable. Otherwise snaps it to the nearest
    // walkable ground TO THAT POINT ITSELF (an expanding-ring tile search — see
    // NavMeshHandler.GetNearestNodeAt), so a click into open water/void still resolves to
    // roughly where it landed instead of being rejected outright.
    //
    // Deliberately NOT a march/raycast from some other reference point (a troop's position,
    // a selection centroid, an already-clamped click point, etc.) toward this one, stopping
    // at the first non-walkable tile crossed along the way — that used to be exactly this
    // method's behavior, and it silently truncated destinations that were themselves
    // perfectly walkable and reachable (by a route that goes around whatever obstacle the
    // straight line happened to cross — a wall, a lake, an entire separate island only
    // reachable via a bridge) down to wherever that straight line first went unwalkable.
    // Pathfinding.PathFindNavMesh already routes around obstacles correctly on its own once
    // it's actually given the real destination; this must only ever touch a destination that
    // isn't walkable at all.
    private static Vector2 SnapToWalkable(Vector2 point)
    {
        if (IsWalkable(point)) return point;

        NavMeshHandler handler = NavMeshHandler.instance;
        if (handler == null) return point;

        ushort tileX = (ushort)Mathf.FloorToInt(point.x);
        ushort tileY = (ushort)Mathf.FloorToInt(point.y);
        NavMeshNode nearest = handler.GetNearestNodeAt(tileX, tileY, SnapToWalkableSearchRadius);
        if (nearest == null) return point;

        float clampedX = Mathf.Clamp(point.x, nearest.x1, nearest.x2 + 1f);
        float clampedY = Mathf.Clamp(point.y, nearest.y1, nearest.y2 + 1f);
        return new Vector2(clampedX, clampedY);
    }

    private static bool IsWalkable(Vector2 point)
        => NavMeshHandler.instance == null || NavMeshHandler.instance.GetNodeAtWorldCoords(point.x, point.y) != null;

    private void Select(ulong entityId, Color color)
    {
        if (!_selectedEntityIds.Add(entityId)) return;
        _lastSelectedEntityId = entityId;
        if (_selectionObjects.TryGetValue(entityId, out var prefab))
            prefab.SetSelected(color);
    }

    // Deselects everything, then selects ONLY entityId — the keyboard equivalent of a plain
    // (non-additive) click-select, same _friendlySingleSelectedColor a mouse click would use.
    // No ownership/selectability validation here (same trust level Select() itself already
    // has) — used by TroopMarkerManager's double-tap-a-marker-key gesture.
    public void SelectOnly(ulong entityId)
    {
        DeselectAll();
        Select(entityId, _friendlySingleSelectedColor);
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
    // Reads the selection ring's OWN already-computed transform (set this frame by
    // ApplySelectionPosition) rather than re-deriving a position of its own — this used to
    // call _interpolator.Update(targetId, ...) a second time for the same entity, which (since
    // Update mutates that entity's shared per-instance Sample as a side effect) advanced the
    // ring's own lenient chase an extra, uncoordinated step whenever this was also called in
    // the same frame — see _targetingInterpolator's own doc comment for the identical failure
    // mode. Reusing the ring's transform directly sidesteps that entirely and guarantees the
    // camera glides in lockstep with exactly what's on screen, not a second independent guess
    // at the same position.
    public bool TryGetFocusPositionForSelection(out Vector3 worldPos)
    {
        worldPos = default;
        if (_selectedEntityIds.Count == 0) return false;

        ulong targetId = (_lastSelectedEntityId != 0 && _selectedEntityIds.Contains(_lastSelectedEntityId))
            ? _lastSelectedEntityId
            : FirstSelectedId();

        if (targetId == 0 || !_selectionObjects.TryGetValue(targetId, out SelectionPrefab prefab)) return false;

        worldPos = prefab.transform.position;
        return true;
    }

    private ulong FirstSelectedId()
    {
        foreach (ulong id in _selectedEntityIds) return id;
        return 0;
    }

    // Current interpolated world position of ANY selectable entity's own tracking ring — not
    // just the current selection (unlike TryGetFocusPositionForSelection above). Every
    // selectable entity already has a ring/interpolator from SetupSelection regardless of
    // whether it's ever been selected, so this is reused by TroopMarkerManager for its own
    // camera-follow/marker-icon positioning instead of running a second independent
    // TickPositionInterpolator chasing the exact same entity.
    public bool TryGetEntityPosition(ulong entityId, out Vector3 worldPos)
    {
        worldPos = default;
        if (!_selectionObjects.TryGetValue(entityId, out SelectionPrefab prefab)) return false;
        worldPos = prefab.transform.position;
        return true;
    }

    // Tab: cycles the camera through every entity the local player owns (independent of
    // the current selection), advancing to the next one each call and wrapping back to the
    // first once the last is reached — or, with forward false (Shift+Tab), the previous one,
    // wrapping back to the last. Ordered by ascending entity id, which is stable as long as
    // the set of owned entities doesn't change between calls.
    public bool TryGetNextOwnedEntityPosition(out Vector3 worldPos, bool forward = true)
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

        int count = _ownedEntityCycleBuffer.Count;
        int step = forward ? 1 : -1;

        int nextIndex = forward ? 0 : count - 1;
        int lastIndex = _ownedEntityCycleBuffer.IndexOf(_lastCycledOwnedEntityId);
        if (lastIndex >= 0)
            nextIndex = (lastIndex + step + count) % count;

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
        _targetingInterpolator.Remove(e.EntityId);
    }

    public void DeleteSelectionObject(EntityDeletedEvent e)
    {
        _selectedEntityIds.Remove(e.EntityId);
        DestroySelectionObject(e.EntityId);
        DestroyTargetingObject(e.EntityId);
        _interpolator.Remove(e.EntityId);
        _targetingInterpolator.Remove(e.EntityId);
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

    // Same closed-loop "chase the true tick position at the entity's own Speed stat" style
    // BasicTroopRenderer/VariedAttackTroopRenderer use for the 3D model itself (see
    // TickPositionInterpolator's own doc comment on why that's self-correcting where the
    // old strict-lerp-only approach could drift) — falls back to the plain strict-lerp
    // overload while not moving, teleported, or displaced (a knockback's velocity isn't
    // bounded by the Speed stat, so chasing at Speed could lag behind it — same guard
    // BasicTroopRenderer's own branch uses).
    public void ApplySelectionPosition(ulong entityId, ref PositionComponent pos, SelectionPrefab selection)
    {
        selection.transform.position = InterpolatedPosition(_interpolator, entityId, pos);
    }

    public void ApplyTargetingPosition(ulong entityId, ref PositionComponent pos, TargetingPrefab targeting)
    {
        targeting.transform.position = InterpolatedPosition(_targetingInterpolator, entityId, pos);
    }

    private Vector3 InterpolatedPosition(TickPositionInterpolator interpolator, ulong entityId, PositionComponent pos)
    {
        Vector3 worldPos = WorldPositionFor(pos);
        bool isMoving = IsMoving(entityId);
        bool teleported = IsTeleported(entityId);

        if (isMoving && !IsDisplaced(entityId))
        {
            float speedWorldUnitsPerSecond = StatsQuery.GetSpeed(_ecs, entityId, DefaultSpeed) / StatsQuery.SpeedScale;
            return interpolator.Update(entityId, worldPos, isMoving, teleported, speedWorldUnitsPerSecond);
        }

        return interpolator.Update(entityId, worldPos, isMoving, teleported);
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        // Deliberately above TerrainPatchRegistry's own +0.01 ground offset — these rings
        // are Opaque/ZWrite-on while ground patches are Transparent/ZWrite-off, so sitting
        // at the exact same height caused a depth-test coin flip (z-fighting) that let
        // patches render on top of the ring from some camera angles. A small but clearly
        // distinct offset makes the ring the deterministic winner instead.
        return new Vector3(pos.X, height + 0.03f, pos.Y);
    }

    private bool IsMoving(ulong entityId)
        => _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).IsMoving;

    private bool IsTeleported(ulong entityId)
        => _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).TeleportedTick == _ecs.CurrentSimulationTick;

    private bool IsDisplaced(ulong entityId)
        => _movableStore != null && _movableStore.HasComponent(entityId)
            && _movableStore.GetComponent(entityId).IsDisplaced;
}
