// Deploy/activation delay shared by every SpawnAtPointCard-spawned entity (troop,
// building, spell, ...) — see ActivationSystem, which counts _ticksUntilActive down and
// fires EntityActivatedEvent once it crosses zero. Kept as its own component (rather than
// living on TroopComponent, which only troops/buildings have) so any future card kind gets
// the same client/server activation sync for free just by adding this.
public struct ActivatableComponent : IComponent
{
    public ulong _ticksUntilActive;
    // Snapshot of _ticksUntilActive at spawn time, set once and never decremented — lets
    // anything showing deploy progress (e.g. DeployProgressIndicatorManager) compute a
    // 1→0 ratio without needing to know the card's activation delay itself.
    public ulong InitialTicksUntilActive;
    public bool IsActive => _ticksUntilActive == 0;
    public long TicksUntilActive => (long)_ticksUntilActive;
}
