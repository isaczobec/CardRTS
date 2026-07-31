// Marks a modifier entity as one that should end early the instant whatever it targets
// takes any damage — see RemoveModifierOnDamageSystem. Generic (not specific to any one
// card's effect) so any future "breaks on damage taken" debuff/buff can reuse it the same
// way multiple existing effects reuse StatModifierComponent/ModifierComponent themselves,
// rather than each needing its own bespoke "wakes up" system.
public struct RemoveModifierOnDamageComponent : IComponent
{
}
