public struct SeekingProjectileComponent : IComponent
{
    // The entity this projectile homes in on.
    public ulong TargetEntityId;

    // Travel speed in milli-tiles per second (1000 = 1 tile/second) — an integer unit,
    // like other stat/duration fields in this codebase, rather than a raw float rate.
    // See SeekingProjectileSystem for how this is converted to a per-tick step.
    public int Speed;
}
