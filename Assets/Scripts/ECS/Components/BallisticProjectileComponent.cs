// Payload for a cosmetic-only projectile entity fired by a TurretAIComponent in
// TurretProjectileMode.Ballistic (see TurretAISystem.FireBallistic/MissileSiloCard) — a
// straight/arced flight with no hitbox at all. PositionComponent holds the LAUNCH position
// (set once, never touched again — all per-frame flight interpolation is purely visual, done
// by MissileProjectileRenderer from LifetimeComponent's own elapsed-fraction, exactly the
// same 0..1 ratio AoeSpellRenderer computes for its own duration-elapsed material property);
// TargetX/TargetY is where it's headed. The actual AOE damage on arrival is a SEPARATE
// ScheduledCallSystem call, scheduled for this entity's own flight duration at the same time
// this entity was created (see TurretAISystem.FireBallistic) — not something this entity (or
// its own LifetimeComponent expiry) triggers directly.
public struct BallisticProjectileComponent : IComponent
{
    public float TargetX;
    public float TargetY;

    // AOE radius (world units) this projectile will deal damage in once it lands — captured
    // once at launch time purely so MissileProjectileRenderer can scale its target-ground
    // indicator to the correct size without needing to know the launching card's own
    // constants.
    public float ImpactRadius;
}
