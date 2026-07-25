// Payload on a pooled projectile entity (alongside SeekingProjectileComponent/
// SkillshotProjectileComponent) — applies EffectType to whatever it hits, in addition to
// the normal DamageRequest every projectile already deals (see ProjectileOnHitSystem,
// called from SeekingProjectileSystem/SkillshotProjectileSystem at their own hit-detection
// points). DurationSeconds is shared (how long the resulting modifier lasts); the rest of
// the fields are specific to one effect type and ignored by the others, the same way this
// struct grew to fit Burn alongside the pre-existing Slow.
public struct ProjectileOnHitComponent : IComponent
{
    public ProjectileOnHitEffectType EffectType;

    // How long the resulting modifier lasts (Slow's Chilled, Burn's Scorched) before it
    // needs refreshing by another hit.
    public float DurationSeconds;

    // Slow-specific: fraction (negative) the target's Speed is reduced by — e.g. -0.3 =
    // 30% slower.
    public float SlowRatio;

    // Burn-specific — see ProjectileOnHitSystem.ApplyBurn/ApplyScorch. Damage per proc is
    // BurnBaseDamagePerProc + Stacks * BurnDamagePerStackRatio * (shooter's own Damage
    // stat), Stacks capped at BurnMaxStacks. BurnSplashRangeMultiplier is a multiple of the
    // shooter's own Range stat, applied around the hit target to also scorch nearby enemies.
    public float BurnBaseDamagePerProc;
    public float BurnDamagePerStackRatio;
    public int BurnMaxStacks;
    public int BurnProcPeriodTicks;
    public float BurnSplashRangeMultiplier;

    // Aoe-specific — see ProjectileOnHitSystem.ApplyAoe. Radius is AoeRadiusMultiplier x the
    // shooter's own Range stat, centered on the hit point; every enemy troop caught in it
    // (including whatever the projectile directly hit, which still also takes the normal
    // per-shot DamageRequest every projectile deals) takes AoeDamageRatio x the shooter's own
    // Damage stat as instant damage.
    public float AoeRadiusMultiplier;
    public float AoeDamageRatio;
}
