// Payload on a pooled projectile entity (alongside SeekingProjectileComponent/
// SkillshotProjectileComponent) — applies EffectType to whatever it hits, in addition to
// the normal DamageRequest every projectile already deals (see ProjectileOnHitSystem,
// called from SeekingProjectileSystem/SkillshotProjectileSystem at their own hit-detection
// points). SlowRatio/DurationSeconds are the Slow effect's own parameters — not addressed
// now, but a future effect type needing different parameters would either reuse these
// generically or this struct would grow to fit it, since Slow is the only effect that
// exists yet.
public struct ProjectileOnHitComponent : IComponent
{
    public ProjectileOnHitEffectType EffectType;

    // Slow-specific: fraction (negative) the target's Speed is reduced by — e.g. -0.3 =
    // 30% slower — and how long the slow lasts.
    public float SlowRatio;
    public float DurationSeconds;
}
