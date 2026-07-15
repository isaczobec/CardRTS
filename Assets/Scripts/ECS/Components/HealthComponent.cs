public struct HealthComponent : IComponent
{
    public int CurrentHealth;

    // Entity that most recently damaged this one, set by DamageRequest.Execute. Defaults
    // to 0 ("none" — no entity ID is ever 0, see ECS.NextEntityId) until the first hit;
    // after that it may be DamageRequest.NO_DEALER_ENTITYID if that hit didn't specify a
    // dealer. Read by OnDeathResourceDropSystem to credit a kill.
    public ulong LastDamageDealer;

    // Ticks remaining during which this entity cannot be hit again by a radius-based,
    // multi-hit check that already hit it (see SkillshotProjectileSystem) — decremented
    // once per tick by HitboxImmunitySystem regardless of source. While > 0, that kind of
    // check skips this entity even if it's still within the hit radius, so e.g. a piercing
    // projectile doesn't deal damage to the same target on every single tick it overlaps it.
    public int HitboxImmunityTicksRemaining;

    // How many ticks of HitboxImmunityTicksRemaining this entity's own hits grant to
    // whatever they hit — read off the attacking entity's HealthComponent (e.g. a troop
    // that owns a fired SkillshotProjectileComponent) when a hit lands. 0 (the struct
    // default) means no immunity is granted, so every overlapping tick would land another
    // hit — a piercing attack must set this explicitly at spawn.
    public int HitboxImmunityTicksToGive;
}
