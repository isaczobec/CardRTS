using System.Collections.Generic;
using UnityEngine;

// Reads Q/W/E/R/T/Y/U/I each frame against the LOCAL PLAYER's ability bar (see
// AbilityBarComponent — up to 8 distinct ability types, appended to as the player buys
// ability-granting cards, in visual left-to-right == key order) and, on a key's release,
// resolves which of the player's own troops actually cast that ability via
// AbilityCasterTargeting.FindCasters (per-ability CasterSelectionRadius/
// MaxSimultaneousCasters/PrioritizeSelectedTroops — see Ability's own doc comments) — holding
// the key first gives AbilityIndicatorManager a chance to preview the resolved caster(s)
// (range circle, cursor circle, direction arrow, target indicator) before the cast actually
// fires. No longer tied to troop selection at all — SelectionManager.SelectedEntityIds only
// matters for a PrioritizeSelectedTroops ability's own restriction, not for whether casting is
// possible in the first place.
//
// Purely input-side: no cast/range/cooldown validation happens here (that's AbilitySystem's
// job, since it must never trust the client) — this only decides which InputBase subtype(s) to
// build, once per resolved caster, and — for a TargetLocation/TargetEntity ability — resolves
// each caster's own cast point/target (clamped/filtered per Ability's own fields — see
// AbilityTargeting/EntityTargeting) to send along; an invalid cast (on cooldown, out of range,
// ...) just gets rejected server-side with a logged warning, same as a card play.
//
// A left click anywhere while a key is held cancels that hold's cast entirely (see
// _castCancelled) — the key still needs to be released to end the hold, but nothing gets
// enqueued when it is, and the preview (HeldAbilityId, read by AbilityIndicatorManager)
// disappears the instant the click lands rather than lingering until release.
public class AbilityInputManager : Singleton<AbilityInputManager>
{
    private static readonly KeyCode[] SlotKeys =
    {
        KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y, KeyCode.U, KeyCode.I,
    };

    // The ability bar slot's ability id whose key is currently held, if any (0 otherwise) —
    // read by AbilityIndicatorManager to know which ability (if any) to preview this frame.
    // Only one slot is tracked at a time; holding multiple keys at once just tracks whichever
    // is found first (Q, then W, ... then I) — same simplification the old 4-key version used.
    // Reports 0 while _castCancelled is set too, so a cancelled hold's preview disappears
    // immediately instead of misleadingly implying the cast will still fire on release.
    public int HeldAbilityId => _castCancelled ? 0 : _heldAbilityId;

    private int _heldAbilityId;

    // Set the instant a left click lands anywhere while a key is held (see Update) — the
    // player changed their mind mid-hold. Cleared back to false whenever a fresh hold starts
    // (see UpdateHeld) or nothing is held at all (see ClearHeld).
    private bool _castCancelled;

    // Scratch buffers, reused across casts/frames rather than allocated per call — this
    // class's own, not shared with AbilityIndicatorManager's identically-purposed buffers
    // (see EntityTargeting/AbilityCasterTargeting's own "caller-owned" doc comments on why).
    private readonly List<ulong> _targetQueryBuffer = new List<ulong>();
    private readonly List<ulong> _casterBuffer = new List<ulong>();

    public void Initialize() { }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) { ClearHeld(); return; }
        if (DevConsole.IsOpen) { ClearHeld(); return; }

        ECS ecs = TickManager.instance.ActiveECS;
        ushort localPlayerId = LocalPlayerId();

        ulong barEntityId = AbilityBarHelper.FindPlayerAbilityBarEntity(ecs, localPlayerId);
        ComponentStore<AbilityBarComponent> barStore = ecs.GetComponentStore<AbilityBarComponent>();
        if (barEntityId == 0 || barStore == null || !barStore.HasComponent(barEntityId)) { ClearHeld(); return; }

        AbilityBarComponent bar = barStore.GetComponent(barEntityId);

        // Captured into wasCancelled BEFORE UpdateHeld runs below — a key release and
        // UpdateHeld/ClearHeld's own _castCancelled reset (getting ready for the NEXT hold)
        // can land in the very same frame as this release, so the release loop below must use
        // the value as of THIS frame, not whatever it gets reset to afterward.
        if (_heldAbilityId != 0 && Input.GetMouseButtonDown(0))
            _castCancelled = true;
        bool wasCancelled = _castCancelled;

        for (int slot = 0; slot < SlotKeys.Length; slot++)
        {
            if (!Input.GetKeyUp(SlotKeys[slot])) continue;

            int abilityId = bar.GetAbilityId(slot);
            if (abilityId != 0 && !wasCancelled)
                TryCast(ecs, abilityId, localPlayerId);
        }

        UpdateHeld(bar);
    }

    private void UpdateHeld(AbilityBarComponent bar)
    {
        for (int slot = 0; slot < SlotKeys.Length; slot++)
        {
            if (!Input.GetKey(SlotKeys[slot])) continue;
            if (_heldAbilityId == 0) _castCancelled = false; // a fresh hold just started
            _heldAbilityId = bar.GetAbilityId(slot);
            return;
        }
        ClearHeld();
    }

    private void ClearHeld()
    {
        _heldAbilityId = 0;
        _castCancelled = false;
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    private void TryCast(ECS ecs, int abilityId, ushort localPlayerId)
    {
        if (!AbilityManager.TryGet(abilityId, out Ability ability)) return;
        if (!TileSpaceMouse.TryGetPosition(out float cursorX, out float cursorY)) return;

        IReadOnlyCollection<ulong> selected = SelectionManager.instance != null ? SelectionManager.instance.SelectedEntityIds : null;
        AbilityCasterTargeting.FindCasters(ecs, abilityId, ability, cursorX, cursorY, localPlayerId, selected, _casterBuffer);
        if (_casterBuffer.Count == 0) return;

        foreach (ulong casterId in _casterBuffer)
            CastFromCaster(ecs, casterId, abilityId, ability, cursorX, cursorY, localPlayerId);
    }

    private void CastFromCaster(ECS ecs, ulong casterId, int abilityId, Ability ability, float rawX, float rawY, ushort localPlayerId)
    {
        if (ability.Type == AbilityType.Instant)
        {
            InputBuffer.EnqueueInput(new AbilityUsedInput { AbilityId = abilityId, CastingEntityId = casterId });
        }
        else if (ability.Type == AbilityType.TargetLocation)
        {
            AbilityTargeting.ResolveCastPoint(ecs, casterId, ability, rawX, rawY, out float x, out float y);
            InputBuffer.EnqueueInput(new AbilityUsedAtLocationInput { AbilityId = abilityId, CastingEntityId = casterId, X = x, Y = y });
        }
        else if (ability.Type == AbilityType.TargetEntity)
        {
            ulong targetId = EntityTargeting.FindClosestSelectable(
                ecs, rawX, rawY, ability.TargetSelectionRadius, localPlayerId,
                ability.CanTargetFriendly, ability.CanTargetEnemyOrNeutral, _targetQueryBuffer);
            if (targetId == 0) return;

            // Pre-check the caster range client-side too, so an obviously-out-of-range
            // target never gets sent at all — AbilitySystem.ExecuteOnEntity re-validates
            // this regardless, since the client is never trusted.
            if (!IsWithinCasterRange(ecs, casterId, targetId, ability.Range)) return;

            InputBuffer.EnqueueInput(new AbilityUsedOnEntityInput { AbilityId = abilityId, CastingEntityId = casterId, TargetEntityId = targetId });
        }
    }

    private static bool IsWithinCasterRange(ECS ecs, ulong casterId, ulong targetId, float range)
    {
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(casterId) || !posStore.HasComponent(targetId)) return false;

        PositionComponent casterPos = posStore.GetComponent(casterId);
        PositionComponent targetPos = posStore.GetComponent(targetId);
        float dx = targetPos.X - casterPos.X, dy = targetPos.Y - casterPos.Y;
        return dx * dx + dy * dy <= range * range;
    }
}
