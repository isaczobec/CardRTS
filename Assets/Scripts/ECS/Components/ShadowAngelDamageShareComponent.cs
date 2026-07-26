// Attach alongside a ModifierComponent (see ShadowAngelDamageShareSystem) on a modifier
// entity targeting a friendly troop within a Shadow Angel's aura — grants that troop a share
// of the Angel's own incoming damage. SourceEntityId identifies WHICH Shadow Angel granted
// this, mirroring HealSourceComponent's own HealerEntityId (lets multiple simultaneous
// Angels each keep their own separate modifier entity on the same target instead of one
// overwriting another's, and lets ShadowAngelDamageShareSystem know which entity's incoming
// damage this modifier should react to) — just folded onto the same component as the ratio
// itself, rather than a separate paired component, since nothing here needs to work without
// a source.
public struct ShadowAngelDamageShareComponent : IComponent
{
    public ulong SourceEntityId;

    // Fraction of the Shadow Angel's incoming damage this (and every other currently active
    // recipient of the same Angel's aura) shares in — see ShadowAngelDamageShareSystem.
    public float ShareRatio;
}
