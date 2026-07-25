public struct SkillshotProjectileComponent : IComponent
{
    // Unit-ish direction this projectile travels every tick (SkillshotProjectileSystem
    // re-normalizes before using it, so it doesn't have to stay exactly unit length).
    // Plain mutable data rather than something only ProjectilePool.Fire ever touches — a
    // future component (e.g. one that curves a shot) could mutate this directly before
    // SkillshotProjectileSystem runs each tick, without either of them needing to know why.
    public float DirectionX;
    public float DirectionY;

    // Remaining travel distance (world/tile units) before this projectile deactivates back
    // into its pool. Counted down every tick as it moves; like Direction, a future
    // component could also mutate this directly (e.g. to cut a shot short or extend it).
    public float RangeRemaining;

    // Travel speed in milli-tiles per second (1000 = 1 tile/second), same convention as
    // SeekingProjectileComponent.Speed.
    public int Speed;

    // Radius (world/tile units) used each tick to find entities to hit as this projectile
    // travels — unlike SeekingProjectileComponent's single-target homing/impact, this can
    // overlap (and damage, subject to HealthComponent.HitboxImmunityTicksRemaining)
    // multiple entities over its flight, piercing through rather than stopping on the
    // first hit.
    public float HitRadius;

    // Multiplies the owning troop's own Damage stat for this projectile kind specifically
    // (see SkillshotProjectileSystem) — e.g. an ability-fired pool dealing more than the
    // troop's ordinary auto-attack. Set via ProjectilePool.CreateSkillshotPool's own
    // damageMultiplier param at pool-creation time (always explicitly, defaulting to 1
    // there) rather than left to default here, since a bare 0 would silently zero out
    // damage for anyone who forgot to set it.
    public float DamageMultiplier;

    // If true, this projectile deactivates the instant it hits ANYTHING, instead of
    // piercing on until RangeRemaining runs out — e.g. PirateCard's Hook, which should grab
    // one troop and stop, not pierce through everyone in its path the way a normal skillshot
    // does. Defaults to false (every existing skillshot pool's own piercing behavior,
    // unchanged) — set via ProjectilePool.CreateSkillshotPool's own stopOnFirstHit param.
    public bool StopOnFirstHit;
}
