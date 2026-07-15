using UnityEngine;

// Reads Q/W/E/R each frame and, while exactly one friendly troop is selected (see
// SelectionManager), casts whichever ability is equipped in that slot on the selected
// entity. With zero or more than one troop selected, ability casting is disabled entirely
// — no ability input is enqueued. Purely input-side: no cast/range/cooldown validation
// happens here (that's AbilitySystem's job, since it must never trust the client) — this
// only decides which InputBase subtype to build and, for a TargetLocation ability, reads
// the current cursor position to send along; an invalid cast (on cooldown, out of range,
// ...) just gets rejected server-side with a logged warning, same as a card play.
//
// No screen/world-space casting UI (indicator, range preview, cooldown display, ...) yet
// — see AoeSpellCard/CardRangeIndicatorManager for the equivalent card-side visuals if/when
// this needs the same treatment.
public class AbilityInputManager : Singleton<AbilityInputManager>
{
    private static readonly KeyCode[] SlotKeys = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R };

    public void Initialize() { }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (DevConsole.IsOpen) return;
        if (SelectionManager.instance == null || SelectionManager.instance.SelectedEntityIds.Count != 1) return;

        ulong casterId = GetOnlySelected();
        if (casterId == 0) return;

        for (int slot = 0; slot < SlotKeys.Length; slot++)
            if (Input.GetKeyDown(SlotKeys[slot]))
                TryCast(casterId, slot);
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
            if (!TileSpaceMouse.TryGetPosition(out float x, out float y)) return;
            InputBuffer.EnqueueInput(new AbilityUsedAtLocationInput { AbilityId = abilityId, CastingEntityId = casterId, X = x, Y = y });
        }
    }
}
