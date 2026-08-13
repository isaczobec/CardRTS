using System.Collections.Generic;
using UnityEngine;

// Lets the player recall one of their own troops/buildings back to their deck: hold G while
// hovering the mouse within _hoverRadius of it for _holdThresholdSeconds to begin an
// 8-second recall channel (see RecallSystem) — the same "hold a key near a troop" gesture
// TroopMarkerManager uses for its own camera-marker binding, just with a hold-to-confirm
// delay instead of firing on every held frame, so a stray tap of G never starts a channel by
// accident.
//
// Purely a one-shot trigger: once the hold threshold is reached, a single RecallInput is
// sent and this resets — the 8-second channel itself runs entirely simulation-side
// (RecallSystem) and does NOT require G to still be held afterward. Releasing G, moving the
// cursor off the troop, or letting go before the threshold is reached simply cancels the
// (not yet sent) gesture with no effect — it never touches a channel already in progress;
// cancelling THAT is RecallSystem's own job (damage, or a fresh move/attack/ability order).
//
// NOTE: like every other manager RenderingSetup.SetupRendering() initializes, this requires
// a GameObject carrying this component to already exist in the gameplay scene (Singleton<T>
// only ever sets `instance` from an Awake() on a live instance — see TroopMarkerManager/
// AbilityInputManager for the existing examples) — add one (and add
// `RecallInputManager.instance.Initialize();` alongside the others in RenderingSetup) before
// this can run.
public class RecallInputManager : Singleton<RecallInputManager>
{
    // How close (world/tile units) the cursor must be to a friendly troop for a held G to
    // count as hovering it — mirrors TroopMarkerManager's own _assignRadius role.
    [SerializeField] private float _hoverRadius = 6f;

    // How long (seconds) G must be held continuously, while still hovering the SAME troop,
    // before a recall actually begins.
    [SerializeField] private float _holdThresholdSeconds = 0.5f;

    private ulong _hoveredEntityId;
    private float _holdTime;
    private bool _sent;

    private readonly List<ulong> _queryBuffer = new List<ulong>();

    public void Initialize() { }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) { Reset(); return; }
        if (DevConsole.IsOpen) { Reset(); return; }

        if (!Input.GetKey(KeyCode.G)) { Reset(); return; }

        ECS ecs = TickManager.instance.ActiveECS;
        ushort localPlayerId = NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

        ulong hoveredId = FindHoveredOwnTroop(ecs, localPlayerId);
        if (hoveredId != _hoveredEntityId)
        {
            _hoveredEntityId = hoveredId;
            _holdTime = 0f;
            _sent = false;
        }

        if (_hoveredEntityId == 0) return;

        _holdTime += Time.deltaTime;
        if (_sent || _holdTime < _holdThresholdSeconds) return;

        _sent = true;
        InputBuffer.EnqueueInput(new RecallInput { EntityId = _hoveredEntityId });
    }

    private ulong FindHoveredOwnTroop(ECS ecs, ushort localPlayerId)
    {
        if (!TileSpaceMouse.TryGetPosition(out float x, out float y)) return 0;
        return EntityTargeting.FindClosestSelectable(ecs, x, y, _hoverRadius, localPlayerId,
            canTargetFriendly: true, canTargetEnemyOrNeutral: false, _queryBuffer);
    }

    private void Reset()
    {
        _hoveredEntityId = 0;
        _holdTime = 0f;
        _sent = false;
    }
}
