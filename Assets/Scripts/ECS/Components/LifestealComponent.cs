// Granted by LifestealUpgrade — attached to a permanent modifier entity (ModifierComponent.
// TargetEntityId = the troop it was equipped on, TicksRemaining = int.MaxValue) rather than
// directly to the troop itself, mirroring DamageOverTimeComponent/HealModifierComponent's own
// "payload lives on the modifier entity" shape. A dedicated component instead of reusing
// OnHitScheduleComponent, so this upgrade can be equipped on ANY card without colliding with
// that card's own, unrelated use of OnHitScheduleComponent (e.g. Healer Guardian, Stalker,
// Santa Claus all already carry one for their own on-hit effects). See LifestealSystem, which
// finds this modifier via ModifierQuery.FindActiveModifierId<LifestealComponent>.
public struct LifestealComponent : IComponent
{
    // Fraction of damage dealt that heals this troop back — e.g. 0.25 = 25% lifesteal.
    public float LifestealRatio;
}
