// Payload for a ModifierComponent-carrying modifier entity (same "separate entity per
// modifier" shape as StatModifierComponent/PeriodicDamageReductionComponent) — gives its
// target a pool of absorption health that fully negates incoming damage (see BarrierSystem)
// until that pool itself runs out, at which point the modifier expires early on its own
// (independent of ModifierComponent.TicksRemaining).
public struct BarrierComponent : IComponent
{
    public float MaxHealth;

    // Depleted by BarrierSystem on every hit this barrier absorbs — lives on the modifier
    // entity's own component (not a system-instance field) so it survives a client-
    // prediction reconciliation rewind correctly, matching this codebase's established
    // pattern for any state that must persist across ticks (see e.g. PeriodicDamageReduction
    // Component.HitsTaken).
    public float HealthRemaining;

    // How much of an absorbed hit's (already-mitigated) Amount is actually drawn from
    // HealthRemaining — e.g. 0.5 means the barrier only loses half as much health as the
    // damage it blocked, 2 means it loses twice as much. 1 = drains exactly the blocked
    // amount.
    public float DamageMultiplier;

    // Flat amount drawn from HealthRemaining on top of Amount * DamageMultiplier, on every
    // hit this barrier absorbs (regardless of that hit's own Amount) — e.g. a small constant
    // chip cost per hit even against 0-damage pokes.
    public float DamageAdditiveBonus;
}
