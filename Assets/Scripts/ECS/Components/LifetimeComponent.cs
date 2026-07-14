// Counts down to entity expiry — see LifetimeSystem. TicksRemaining is authored as a
// seconds-based duration at spawn time (via TickManager.SecondsToTicks) and stored in
// ticks, matching every other tick-based countdown in the codebase (ActivatableComponent,
// RespawnableInPlaceComponent).
public struct LifetimeComponent : IComponent
{
    public int TicksRemaining;

    // Snapshot of TicksRemaining at spawn time, set once and never decremented — lets
    // anything showing elapsed duration (e.g. AoeSpellRenderer's _DurationElapsed) compute
    // a 0→1 ratio without needing to know this entity's lifetime itself.
    public int InitialTicksRemaining;
}
