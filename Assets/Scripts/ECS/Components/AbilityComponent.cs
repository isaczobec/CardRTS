// Up to 4 equipped abilities (Q/W/E/R slots — see AbilityInputManager), each an ability ID
// looked up via AbilityManager, its own cooldown length, and its own remaining cooldown.
// Cooldown length lives here (not on the Ability definition) so different troops/entities
// can equip the same ability with a different cooldown. Ticked down by
// AbilityCooldownSystem and reset (to the slot's own CooldownTicks) by AbilitySystem on a
// successful cast. 0 in an AbilityXId field means that slot is empty (ability IDs are
// assigned starting at 1, same convention as entity IDs never being 0 — see
// AbilityManager).
public struct AbilityComponent : IComponent
{
    public int Ability1Id;
    public int Ability2Id;
    public int Ability3Id;
    public int Ability4Id;

    public int Ability1CooldownTicks;
    public int Ability2CooldownTicks;
    public int Ability3CooldownTicks;
    public int Ability4CooldownTicks;

    public int Ability1CooldownTicksRemaining;
    public int Ability2CooldownTicksRemaining;
    public int Ability3CooldownTicksRemaining;
    public int Ability4CooldownTicksRemaining;

    // Charges: how many times a slot can be cast before needing to recharge — e.g. a
    // troop that can fire off 3 shots in quick succession (each still gated by the slot's
    // own CooldownTicks, the minimum time between individual casts), instead of being
    // fully locked out between every single use the way an ordinary MaxCharges == 1
    // ability is. 0 (unset) is treated as 1 by GetMaxCharges — the "ordinary
    // single-use-then-cooldown" ability every existing card already grants, entirely via
    // CooldownTicks/CooldownTicksRemaining above, with charges never consulted at all (see
    // AbilitySystem.TryBeginCast/CommitCooldown). A card granting MaxCharges > 1 must also
    // explicitly set ChargesRemaining (typically to the same value, starting full) and
    // ChargeCooldownTicks — unlike MaxCharges, there's no defaulting for those two.
    public int Ability1MaxCharges;
    public int Ability2MaxCharges;
    public int Ability3MaxCharges;
    public int Ability4MaxCharges;

    public int Ability1ChargesRemaining;
    public int Ability2ChargesRemaining;
    public int Ability3ChargesRemaining;
    public int Ability4ChargesRemaining;

    // Time to regenerate ONE charge — only one charge regenerates at a time (see
    // AbilityChargeSystem): the countdown doesn't (re)start until ChargesRemaining first
    // drops below MaxCharges, and restarts immediately for the next charge once one
    // finishes, until ChargesRemaining reaches MaxCharges again.
    public int Ability1ChargeCooldownTicks;
    public int Ability2ChargeCooldownTicks;
    public int Ability3ChargeCooldownTicks;
    public int Ability4ChargeCooldownTicks;

    public int Ability1ChargeCooldownTicksRemaining;
    public int Ability2ChargeCooldownTicksRemaining;
    public int Ability3ChargeCooldownTicksRemaining;
    public int Ability4ChargeCooldownTicksRemaining;

    // Which of this component's 4 slots (if any) has abilityId equipped, or -1 if none does.
    // Shared by AbilitySystem.TryBeginCast (via AbilityEligibility.CanCast) and
    // AbilityCasterTargeting, so both agree on the same slot for the same ability.
    public int FindSlot(int abilityId)
    {
        for (int slot = 0; slot < 4; slot++)
            if (GetAbilityId(slot) == abilityId)
                return slot;
        return -1;
    }

    // slot is 0-3 (Q/W/E/R). Returns 0 (no ability) for an out-of-range slot.
    public int GetAbilityId(int slot) => slot switch
    {
        0 => Ability1Id,
        1 => Ability2Id,
        2 => Ability3Id,
        3 => Ability4Id,
        _ => 0,
    };

    public int GetCooldownTicks(int slot) => slot switch
    {
        0 => Ability1CooldownTicks,
        1 => Ability2CooldownTicks,
        2 => Ability3CooldownTicks,
        3 => Ability4CooldownTicks,
        _ => 0,
    };

    public int GetCooldownTicksRemaining(int slot) => slot switch
    {
        0 => Ability1CooldownTicksRemaining,
        1 => Ability2CooldownTicksRemaining,
        2 => Ability3CooldownTicksRemaining,
        3 => Ability4CooldownTicksRemaining,
        _ => 0,
    };

    public void SetCooldownTicksRemaining(int slot, int value)
    {
        switch (slot)
        {
            case 0: Ability1CooldownTicksRemaining = value; break;
            case 1: Ability2CooldownTicksRemaining = value; break;
            case 2: Ability3CooldownTicksRemaining = value; break;
            case 3: Ability4CooldownTicksRemaining = value; break;
        }
    }

    // Defaults to 1 when unset (0) — see this struct's own doc comment on the charge
    // fields.
    public int GetMaxCharges(int slot)
    {
        int stored = slot switch
        {
            0 => Ability1MaxCharges,
            1 => Ability2MaxCharges,
            2 => Ability3MaxCharges,
            3 => Ability4MaxCharges,
            _ => 1,
        };
        return stored <= 0 ? 1 : stored;
    }

    public int GetChargesRemaining(int slot) => slot switch
    {
        0 => Ability1ChargesRemaining,
        1 => Ability2ChargesRemaining,
        2 => Ability3ChargesRemaining,
        3 => Ability4ChargesRemaining,
        _ => 0,
    };

    public void SetChargesRemaining(int slot, int value)
    {
        switch (slot)
        {
            case 0: Ability1ChargesRemaining = value; break;
            case 1: Ability2ChargesRemaining = value; break;
            case 2: Ability3ChargesRemaining = value; break;
            case 3: Ability4ChargesRemaining = value; break;
        }
    }

    public int GetChargeCooldownTicks(int slot) => slot switch
    {
        0 => Ability1ChargeCooldownTicks,
        1 => Ability2ChargeCooldownTicks,
        2 => Ability3ChargeCooldownTicks,
        3 => Ability4ChargeCooldownTicks,
        _ => 0,
    };

    public int GetChargeCooldownTicksRemaining(int slot) => slot switch
    {
        0 => Ability1ChargeCooldownTicksRemaining,
        1 => Ability2ChargeCooldownTicksRemaining,
        2 => Ability3ChargeCooldownTicksRemaining,
        3 => Ability4ChargeCooldownTicksRemaining,
        _ => 0,
    };

    public void SetChargeCooldownTicksRemaining(int slot, int value)
    {
        switch (slot)
        {
            case 0: Ability1ChargeCooldownTicksRemaining = value; break;
            case 1: Ability2ChargeCooldownTicksRemaining = value; break;
            case 2: Ability3ChargeCooldownTicksRemaining = value; break;
            case 3: Ability4ChargeCooldownTicksRemaining = value; break;
        }
    }
}
