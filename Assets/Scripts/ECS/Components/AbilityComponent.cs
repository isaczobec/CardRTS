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
}
