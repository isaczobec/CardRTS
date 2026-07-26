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

    // Banked damage still owed, drained a little every tick.
    public float StoredDamage;

    // Sub-1-point remainder from a tick's drain amount (DrainPerSecond * TickInterval is
    // rarely a whole number) — carried to the next tick so fractional drain isn't lost to
    // per-tick rounding.
    public float DrainCarry;

    // ECS.CurrentSimulationTick this troop last took a fresh incoming hit — BruiserSystem
    // wipes StoredDamage once this falls too far behind CurrentSimulationTick.
    public ulong LastHitTick;
}
