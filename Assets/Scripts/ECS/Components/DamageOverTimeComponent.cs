// Reusable "deal DamagePerProc damage every PeriodTicks ticks" effect — attach alongside a
// ModifierComponent (see DamageOverTimeSystem) on a modifier entity targeting whoever should
// take the damage. The modifier's own TicksRemaining/ModifierSystem governs when the effect
// as a whole expires; this only tracks the proc cadence within that lifetime. See e.g.
// ProjectileOnHitSystem.ApplyBurn (FireManCard's Scorched debuff) for a concrete use.
public struct DamageOverTimeComponent : IComponent
{
    public int DamagePerProc;
    public int PeriodTicks;
    public int TicksUntilNextProc;

    // For damage attribution/kill credit (DamageRequest.DealerEntityId) — the entity that
    // originally applied this effect, not the modifier entity itself.
    public ulong DealerEntityId;
}
