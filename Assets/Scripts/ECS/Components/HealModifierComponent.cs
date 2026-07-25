// Reusable "heal (Additive + Ratio * MaxHealth) every PeriodTicks ticks" effect — attach
// alongside a ModifierComponent (see HealModifierSystem) on a modifier entity targeting
// whoever should be healed. The modifier's own TicksRemaining/ModifierSystem governs when
// the effect as a whole expires; this only tracks the proc cadence within that lifetime.
// Deliberately carries no notion of "who granted this" — see HealSourceComponent for that,
// kept as a separate, optional component so this one stays usable for any future
// heal-over-time effect that doesn't need per-source stacking/querying. Mirrors
// DamageOverTimeComponent's own shape.
public struct HealModifierComponent : IComponent
{
    public int PeriodTicks;
    public int TicksUntilNextProc;
    public float HealAdditivePerProc;
    public float HealRatioPerProc;
}
