// Payload for a ModifierComponent-carrying modifier entity — fires a pooled projectile
// from the modifier's target (ModifierComponent.TargetEntityId, whoever owns the
// projectile pool) in a fixed direction, on the modifier's very last active tick before it
// expires (see FireProjectileOnExpireSystem). Direction is captured once at cast time
// (rather than re-aiming at a live target each tick) so the shot always fires exactly
// where the caster aimed, even if the intended target moved or the caster has no specific
// target at all (e.g. a ring/volley effect). General-purpose — not tied to skillshots
// specifically, works for any troop with a projectile pool (see
// ProjectilePool.FireInDirection); pair with an ActionWindupComponent on the same modifier
// entity for a "windup, then fire" ability, or use standalone for any other delayed-fire
// effect.
public struct FireProjectileOnExpireComponent : IComponent
{
    public float DirectionX;
    public float DirectionY;
}
