public struct SeekingProjectileComponent : IComponent
{
    // The entity this projectile homes in on.
    public ulong TargetEntityId;

    // Travel speed in milli-tiles per second (1000 = 1 tile/second) — an integer unit,
    // like other stat/duration fields in this codebase, rather than a raw float rate.
    // See SeekingProjectileSystem for how this is converted to a per-tick step.
    public int Speed;

    // 0 (default) means "deal ProjectileBaseComponent.OwnerEntityId's own Damage stat," the
    // original behavior every existing pool still gets. A positive value overrides that with
    // a flat amount instead — e.g. VengefulSpiritsUpgrade's proc projectiles always deal
    // exactly 12 regardless of the wielder's own Damage stat. See ProjectilePool.CreatePool's
    // own fixedDamageOverride param, and SeekingProjectileSystem's own damage calculation.
    public int FixedDamageOverride;

    // What kind of damage instance this pool's hits should be tagged as (see DamageProcType)
    // — Direct (the default) for an ordinary troop/turret auto-attack pool, Secondary for a
    // pool that only ever fires as a side effect of another hit (e.g. VengefulSpiritsUpgrade).
    // Threaded through to the resulting DamageRequest via ProjectileHitRequest.ProcType.
    public DamageProcType ProcType;
}
