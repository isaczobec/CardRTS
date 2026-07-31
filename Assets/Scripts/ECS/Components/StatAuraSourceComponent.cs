// Marks a modifier entity as a granted stat-change aura buff (paired with a
// StatModifierComponent, which is what actually drives the effect — this itself carries no
// stat data, just the scoping below) — SourceEntityId scopes it per-source, the same way
// ShadowAngelDamageShareComponent/HealSourceComponent do for their own granted buffs, so a
// DIFFERENT source's own buff on the same target stacks alongside this one instead of
// overwriting/refreshing it. Generic (not attack-speed-specific) so any future stat-aura
// troop (a defense aura, a damage aura, ...) can reuse it the same way instead of each
// needing its own bespoke source-scoping component. See StrategyConsultantCard's attack
// speed aura, the only one that grants this today.
public struct StatAuraSourceComponent : IComponent
{
    public ulong SourceEntityId;
}
