// Payload for a ModifierComponent-carrying modifier entity — fires a pooled projectile
// from a projectile pool in a fixed direction, on the modifier's very last active tick
// before it expires (see FireProjectileOnExpireSystem). Direction is captured once at cast
// time (rather than re-aiming at a live target each tick) so the shot always fires exactly
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

    // Entity ID of the ProjectileOwnerComponent-carrying pool to fire from — resolved once
    // at cast time (see Ability.ProjectileOwnerIndex/ProjectilePool.ResolveOwnerAtIndex),
    // NOT necessarily the same entity as ModifierComponent.TargetEntityId (the modifier's
    // target is who's winding up/whose position the shot fires from; the pool it fires FROM
    // can be a different entity — e.g. a troop's secondary, faster/longer-range pool). 0
    // falls back to ModifierComponent.TargetEntityId's own pool, for a caller that only has
    // one pool and doesn't need to think about this at all.
    public ulong ProjectilePoolOwnerId;

    // Overrides a skillshot-type pool's own travel distance for this shot, instead of
    // deriving it from ProjectilePool.FireInDirection's usual StatsQuery.GetRange(ownerId)
    // lookup — 0 (the default) means "no override, use the pool owner's own Range stat" (see
    // ProjectilePool.AimSkillshot), unchanged from before this field existed. Needed for a
    // pool whose owner IS the caster itself (ProjectileOwnerIndex = 0 — see PirateCard's Hook
    // ability) but whose intended travel distance is longer than that caster's own melee
    // Range stat, which is also used for unrelated things (e.g. BasicMeleeAISystem's own
    // attack-range check) and so can't just be bumped up to match.
    public float RangeOverride;
}
