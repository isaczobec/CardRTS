using System.Collections.Generic;
using UnityEngine;

// Lets the player bind any of the 7 bottom-row keys (Z X C V B N M) to one of their own
// troops for quick camera navigation — purely a local client-side convenience (not synced/
// networked, same as SelectionManager's own selection set never is), analogous to a MOBA's
// "assign camera hotkey to a hero" binding. See TroopMarkerIconManager for the on-screen
// marker badge each bound key gets.
//
// Each key is dual-purpose, decided by whether it's CURRENTLY bound or not:
//   - Unbound + held + cursor within _assignRadius of a friendly troop — binds this key to
//     that troop (see Update/IsHoveringTroop). An already-bound key is locked against this —
//     hovering a different troop while holding it does nothing, it does NOT rebind — so a
//     binding can only ever change via an explicit clear (Alt + the key, or the troop's own
//     death) followed by a fresh assign.
//   - Bound + held — "follow" mode regardless of where the cursor is: TryGetFollowPosition
//     returns the bound troop's current position every frame, read by
//     CameraController.HandleFollowMarker to re-center the camera exactly the way held Space
//     already does for the selection.
// Double-pressing a key (two presses within _doubleTapWindow — same "quick succession" pattern
// CameraController's own Alt-double-tap-yaw-reset already uses) clears the current selection,
// selects ONLY the bound troop, and snaps the camera straight to it, regardless of where the
// cursor happens to be at that moment.
// Alt + the key clears that key's binding entirely — the only way to free it up for a new
// assignment short of the troop dying, which also clears it automatically (see
// OnTroopDied/OnEntityRemoved).
public class TroopMarkerManager : Singleton<TroopMarkerManager>
{
    public static readonly KeyCode[] MarkerKeys =
    {
        KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N, KeyCode.M,
    };

    // How close (world/tile units) the cursor must be to a friendly troop for an unbound held
    // key to assign to it — mirrors SelectionManager.SingleSelectRadius's own role for
    // click-selection, just more generous since there's no click-precision visual feedback for
    // a keyboard gesture the way there is for a mouse click.
    [SerializeField] private float _assignRadius = 6f;

    // Max gap (seconds) between two presses of the SAME key for the second one to count as a
    // double-tap — mirrors CameraController's own _doubleTapAltResetWindow.
    [SerializeField] private float _doubleTapWindow = 0.3f;

    private ECS _ecs;

    // Entries are removed (not zeroed) when a binding is cleared.
    private readonly Dictionary<KeyCode, ulong> _markers = new Dictionary<KeyCode, ulong>();
    private readonly Dictionary<KeyCode, float> _lastPressTime = new Dictionary<KeyCode, float>();

    private readonly List<ulong> _queryBuffer = new List<ulong>();
    private readonly List<KeyCode> _staleKeysScratch = new List<KeyCode>();

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        // TroopDiedEvent (see DeathRequest) is the general "this troop just died" signal —
        // fires regardless of whether the entity is later fully removed or lingers
        // (respawnable in-place entities, corpses, etc.); EntityDeletedEvent is a catch-all
        // for any other removal path (e.g. a non-troop marked entity, if that's ever possible).
        TickManager.instance.ServerFlagEvents.Subscribe<TroopDiedEvent>(OnTroopDied);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityRemoved);
    }

    // Read by TroopMarkerIconManager to know what to render.
    public bool TryGetMarker(KeyCode key, out ulong troopId) => _markers.TryGetValue(key, out troopId);

    // True + the bound troop's current world position while a marker key that's ALREADY
    // bound is held — an unbound key never has anything to follow (holding it while hovering
    // a troop is instead a fresh-assign gesture, handled in Update). Called every frame from
    // CameraController.HandleFollowMarker. Only the FIRST currently-held marker key (in
    // MarkerKeys order) is considered, same "first held key wins" simplification
    // AbilityInputManager's own held-slot tracking already uses.
    public bool TryGetFollowPosition(out Vector3 worldPos)
    {
        worldPos = default;
        if (DevConsole.IsOpen) return false;

        foreach (KeyCode key in MarkerKeys)
        {
            if (!Input.GetKey(key)) continue;
            if (!_markers.TryGetValue(key, out ulong troopId)) return false;
            if (SelectionManager.instance == null) return false;
            return SelectionManager.instance.TryGetEntityPosition(troopId, out worldPos);
        }

        return false;
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (DevConsole.IsOpen) return;

        // Continuous: (re)bind whichever held key(s) are still UNBOUND and currently hover a
        // friendly troop — an already-bound key is locked (see class doc comment) until
        // explicitly cleared via Alt+key or its troop's own death.
        if (IsHoveringTroop(out ulong hoveredTroopId))
        {
            foreach (KeyCode key in MarkerKeys)
                if (Input.GetKey(key) && !_markers.ContainsKey(key))
                    _markers[key] = hoveredTroopId;
        }

        // Edge-triggered: Alt+key clears that key's binding; two plain presses in quick
        // succession select+focus its bound troop.
        bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        foreach (KeyCode key in MarkerKeys)
        {
            if (!Input.GetKeyDown(key)) continue;

            if (altHeld)
            {
                _markers.Remove(key);
                _lastPressTime.Remove(key);
                continue;
            }

            float now = Time.unscaledTime;
            bool isDoubleTap = _lastPressTime.TryGetValue(key, out float last) && now - last <= _doubleTapWindow;
            if (isDoubleTap)
            {
                _lastPressTime.Remove(key); // consumed — a third tap starts a fresh pair, not another select
                if (_markers.TryGetValue(key, out ulong troopId))
                    SelectAndFocus(troopId);
            }
            else
            {
                _lastPressTime[key] = now;
            }
        }
    }

    private void SelectAndFocus(ulong troopId)
    {
        if (!_ecs.HasEntity(troopId)) return;
        if (SelectionManager.instance == null) return;

        SelectionManager.instance.SelectOnly(troopId);
        if (CameraController.instance != null && SelectionManager.instance.TryGetEntityPosition(troopId, out Vector3 pos))
            CameraController.instance.JumpTo(pos);
    }

    private bool IsHoveringTroop(out ulong troopId)
    {
        troopId = 0;
        if (!TileSpaceMouse.TryGetPosition(out float x, out float y)) return false;

        ushort localPlayerId = NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
        troopId = EntityTargeting.FindClosestSelectable(_ecs, x, y, _assignRadius, localPlayerId,
            canTargetFriendly: true, canTargetEnemyOrNeutral: false, _queryBuffer);
        return troopId != 0;
    }

    private void OnTroopDied(TroopDiedEvent e) => RemoveMarkersFor(e.EntityId);
    private void OnEntityRemoved(EntityDeletedEvent e) => RemoveMarkersFor(e.EntityId);

    private void RemoveMarkersFor(ulong entityId)
    {
        _staleKeysScratch.Clear();
        foreach (KeyValuePair<KeyCode, ulong> kvp in _markers)
            if (kvp.Value == entityId)
                _staleKeysScratch.Add(kvp.Key);

        foreach (KeyCode key in _staleKeysScratch)
            _markers.Remove(key);
    }
}
