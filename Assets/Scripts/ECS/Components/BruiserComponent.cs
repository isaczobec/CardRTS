// Defers a fraction of incoming damage into a banked pool instead of applying it
// immediately, then bleeds that pool out over time instead — attach alongside a
// ModifierComponent (see BruiserSystem) on a modifier entity targeting whoever should be
// protected. StoredDamage is wiped if this troop goes too long without taking a fresh hit
// (BruiserSystem's own ClearAfterSeconds), so the bank doesn't linger once a fight ends.
public struct BruiserComponent : IComponent
{
    // Fraction of each incoming hit diverted into StoredDamage instead of applied immediately.
    public float DeferralRatio;

    // Health points/second at which StoredDamage drains out.
    public float DrainPerSecond;

    // Banked damage still owed, drained in one lump once every second.
    public float StoredDamage;

    // Ticks remaining until the next drain proc — mirrors DamageOverTimeComponent's own
    // TicksUntilNextProc cadence field.
    public int TicksUntilNextDrain;

    // ECS.CurrentSimulationTick this troop last took a fresh incoming hit — BruiserSystem
    // wipes StoredDamage once this falls too far behind CurrentSimulationTick.
    public ulong LastHitTick;
}
