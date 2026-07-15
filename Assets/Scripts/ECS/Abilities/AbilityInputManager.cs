using UnityEngine;

// Reads Q/W/E/R each frame and, while exactly one friendly troop is selected (see
// SelectionManager), casts whichever ability is equipped in that slot on the selected
// entity once its key is RELEASED (not pressed) — holding the key first gives
// AbilityIndicatorManager a chance to preview the cast (range circle, cursor circle,
// direction arrow) before it actually fires. With zero or more than one troop selected,
// ability casting is disabled entirely — no ability input is enqueued. Purely input-side:
// no cast/range/cooldown validation happens here (that's AbilitySystem's job, since it must
// never trust the client) — this only decides which InputBase subtype to build and, for a
// TargetLocation ability, resolves the cast point (clamped per
// Ability.ClampCastLocationToRange — see AbilityTargeting) to send along; an invalid cast
// (on cooldown, out of range, ...) just gets rejected server-side with a logged warning,
// same as a card play.
public class AbilityInputManager : Singleton<AbilityInputManager>
{
    private static readonly KeyCode[] SlotKeys = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R };

    // The entity/slot whose key is currently held, if any (0 / -1 otherwise) — read by
    // AbilityIndicatorManager to know which ability (if any) to preview this frame. Only
    // one slot is tracked at a time; holding multiple keys at once just tracks whichever
    // is found first (Q, then W, then E, then R).
    public ulong HeldCasterId { get; private set; }
    public int HeldSlot { get; private set; } = -1;

    public void Initialize() { }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) { ClearHeld(); return; }
        if (DevConsole.IsOpen) { ClearHeld(); return; }
        if (SelectionManager.instance == null || SelectionManager.instance.SelectedEntityIds.Count != 1) { ClearHeld(); return; }

        ulong casterId = GetOnlySelected();
        if (casterId == 0) { ClearHeld(); return; }

        UpdateHeldSlot(casterId);

        for (int slot = 0; slot < SlotKeys.Length; slot++)
            if (Input.GetKeyUp(SlotKeys[slot]))
                TryCast(casterId, slot);
    }

    private void UpdateHeldSlot(ulong casterId)
    {
        for (int slot = 0; slot < SlotKeys.Length; slot++)
        {
            if (Input.GetKey(SlotKeys[slot]))
            {
                HeldCasterId = casterId;
                HeldSlot = slot;
                return;
            }
        }
        ClearHeld();
    }

    private void ClearHeld()
    {
        HeldCasterId = 0;
        HeldSlot = -1;
    }

    private ulong GetOnlySelected()
    {
        foreach (ulong id in SelectionManager.instance.SelectedEntityIds)
            return id;
        return 0;
    }

    private void TryCast(ulong casterId, int slot)
    {
        ECS ecs = TickManager.instance.ActiveECS;
        ComponentStore<AbilityComponent> abilityStore = ecs.GetComponentStore<AbilityComponent>();
        if (abilityStore == null || !abilityStore.HasComponent(casterId)) return;

        int abilityId = abilityStore.GetComponent(casterId).GetAbilityId(slot);
        if (abilityId == 0) return;
        if (!AbilityManager.TryGet(abilityId, out Ability ability)) return;

        if (ability.Type == AbilityType.Instant)
        {
            InputBuffer.EnqueueInput(new AbilityUsedInput { AbilityId = abilityId, CastingEntityId = casterId });
        }
        else if (ability.Type == AbilityType.TargetLocation)
        {
            if (!TileSpaceMouse.TryGetPosition(out float rawX, out float rawY)) return;
            AbilityTargeting.ResolveCastPoint(ecs, casterId, ability, rawX, rawY, out float x, out float y);
            InputBuffer.EnqueueInput(new AbilityUsedAtLocationInput { AbilityId = abilityId, CastingEntityId = casterId, X = x, Y = y });
        }
    }
}
