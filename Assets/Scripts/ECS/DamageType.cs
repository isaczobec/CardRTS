// What a DamageRequest is mitigated by — see ArmorMitigationSystem (Normal is mitigated by
// the target's Armor stat, Spell by its SpellResist stat, same formula for both). Normal is
// the default so every existing DamageRequest call site that doesn't set Type explicitly
// keeps behaving exactly as before this was added.
public enum DamageType : byte
{
    Normal = 0,
    Spell = 1,
}
