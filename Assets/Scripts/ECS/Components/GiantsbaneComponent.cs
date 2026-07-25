// Granted by GiantsbaneUpgrade — attached to a permanent modifier entity (ModifierComponent.
// TargetEntityId = the troop it was equipped on, TicksRemaining = int.MaxValue) rather than
// directly to the troop itself, mirroring DamageOverTimeComponent/HealModifierComponent's own
// "payload lives on the modifier entity" shape. A dedicated component instead of reusing
// OnHitScheduleComponent, so this upgrade can be equipped on ANY card without colliding with
// that card's own, unrelated use of OnHitScheduleComponent (e.g. Healer Guardian, Stalker,
// Santa Claus all already carry one for their own on-hit effects). See GiantsbaneSystem,
// which finds this modifier via ModifierQuery.FindActiveModifierId<GiantsbaneComponent>.
public struct GiantsbaneComponent : IComponent
{
    // Qualifying hits between each proc — see GiantsbaneSystem.
    public int PeriodHits;

    // Running countdown to the next proc — lives on the component (not a system-instance
    // field) so it survives a client-prediction reconciliation rewind correctly, matching
    // OnHitScheduleComponent.HitsUntilProc's own reasoning.
    public int HitsUntilProc;

    // Bonus damage per proc, as a ratio of the TARGET's own max health.
    public float BonusDamageMaxHealthRatio;
}
