// Periodically deals damage to every entity within range — see DamageAuraSystem.
public struct DamageAuraComponent : IComponent
{
    // Multiplier applied to the entity's Range stat to get the aura's actual pulse radius.
    public float RangeMultiplier;

    // Multiplier applied to the entity's AttackSpeed stat to get the actual pulse
    // interval. AttackSpeed is in ticks, the same unit every other card's AttackSpeed
    // already uses (see StatsComponent/TickManager.MillisecondsToTicks).
    public float AttackSpeedMultiplier;

    // Ticks remaining until the next pulse — counts down each tick, reset to the
    // (multiplied) AttackSpeed once a pulse fires.
    public int TicksUntilNextPulse;
}
