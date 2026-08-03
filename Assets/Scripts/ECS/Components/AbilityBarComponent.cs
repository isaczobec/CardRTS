// Player-wide "ability bar" — the up-to-8 DISTINCT ability types (see AbilityManager) this
// player has ever purchased a card that grants (see Card.GrantedAbilityIds), in the order
// first purchased. Independent of which specific troop(s) currently equip/can cast a given
// ability, and independent of whether any troop with it is even alive right now — see
// AbilityCasterTargeting for how a bar slot's hotkey (Q..I, in slot order — see
// AbilityInputManager) resolves to actual casting troops. Added to the player's own entity
// (alongside PlayerDeckComponent/PlayerResourcesComponent — see
// NetworkManager.SpawnPlayerEntity), appended to by AbilityBarHelper.RegisterPurchasedAbilities
// on a successful BuyCardSystem purchase. 0 in a slot means empty (ability ids are assigned
// starting at 1, same convention as AbilityComponent's own slots — see AbilityManager).
public struct AbilityBarComponent : IComponent
{
    public int Slot1AbilityId;
    public int Slot2AbilityId;
    public int Slot3AbilityId;
    public int Slot4AbilityId;
    public int Slot5AbilityId;
    public int Slot6AbilityId;
    public int Slot7AbilityId;
    public int Slot8AbilityId;

    public const int SlotCount = 8;

    // slot is 0-7 (visual left-to-right order — Q/W/E/R/T/Y/U/I, see AbilityInputManager).
    // Returns 0 (empty) for an out-of-range slot.
    public int GetAbilityId(int slot) => slot switch
    {
        0 => Slot1AbilityId,
        1 => Slot2AbilityId,
        2 => Slot3AbilityId,
        3 => Slot4AbilityId,
        4 => Slot5AbilityId,
        5 => Slot6AbilityId,
        6 => Slot7AbilityId,
        7 => Slot8AbilityId,
        _ => 0,
    };

    private void SetAbilityId(int slot, int abilityId)
    {
        switch (slot)
        {
            case 0: Slot1AbilityId = abilityId; break;
            case 1: Slot2AbilityId = abilityId; break;
            case 2: Slot3AbilityId = abilityId; break;
            case 3: Slot4AbilityId = abilityId; break;
            case 4: Slot5AbilityId = abilityId; break;
            case 5: Slot6AbilityId = abilityId; break;
            case 6: Slot7AbilityId = abilityId; break;
            case 7: Slot8AbilityId = abilityId; break;
        }
    }

    public bool Contains(int abilityId)
    {
        for (int slot = 0; slot < SlotCount; slot++)
            if (GetAbilityId(slot) == abilityId)
                return true;
        return false;
    }

    // Appends abilityId to the first empty slot. Returns true if it's now on the bar (either
    // it already was, or there was room to add it) and sets added to whether this call is
    // what put it there; returns false (added left false) only when abilityId wasn't already
    // present AND every slot is already taken by some other ability.
    public bool TryAdd(int abilityId, out bool added)
    {
        added = false;
        if (Contains(abilityId)) return true;

        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (GetAbilityId(slot) != 0) continue;
            SetAbilityId(slot, abilityId);
            added = true;
            return true;
        }

        return false;
    }
}
