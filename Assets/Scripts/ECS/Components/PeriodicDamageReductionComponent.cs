// Payload for a ModifierComponent-carrying modifier entity (see e.g. StatModifierComponent/
// DamageBoostUpgrade for the same "separate entity per modifier" shape) — reduces its
// target's incoming damage by ReductionRatio on every HitInterval-th hit it takes. Grant it
// indefinitely by giving the paired ModifierComponent TicksRemaining = int.MaxValue and no
// ActivatableComponent (see IronKnightCard).
public struct PeriodicDamageReductionComponent : IComponent
{
    // Every Nth incoming hit is reduced — e.g. 3 means the 3rd, 6th, 9th, ... hit taken.
    // <= 0 disables the effect entirely (no hit ever triggers).
    public int HitInterval;

    // Fraction (0-1) a triggering hit's damage is reduced by — e.g. 0.35 = 35% less damage.
    public float ReductionRatio;

    // Running count of hits taken so far — lives on the modifier entity's own component
    // (not a system-instance field) so it survives a client-prediction reconciliation
    // rewind correctly, matching this codebase's established pattern for any state that
    // must persist across ticks (see e.g. MovableComponent.TeleportedTick).
    public int HitsTaken;
}
