// Payload for a ModifierComponent-carrying modifier entity — while active, makes its
// target (ModifierComponent.TargetEntityId) untargetable by anyone other than its own
// owner (see ShadowCloakSystem/AbilityManager's Shadow Cloak ability, StalkerCard).
// Deliberately does NOT stop an already-fired projectile (seeking or hitbox) from landing
// — SeekingProjectileSystem/SkillshotProjectileSystem never consult this, only future
// target ACQUISITION does (see ShadowCloakSystem's own doc comment).
public struct ShadowCloakComponent : IComponent
{
    // How many separate damage instances (not cumulative Amount — see ShadowCloakSystem's
    // own DamageRequest.SubscribeExecuted handler) the cloak can absorb before it breaks
    // early, ending it immediately instead of running out its own duration — explicit
    // design ask for AbilityManager's Shadow Cloak ability. 0 (the struct default) means
    // "never breaks from damage," so any future grant that doesn't set this keeps this
    // component's original (damage-immune-to-breaking) behavior.
    public int MaxDamageInstancesBeforeBreak;

    // Running count toward MaxDamageInstancesBeforeBreak — lives on the component (not a
    // system-instance field) so it survives a client-prediction reconciliation rewind
    // correctly, matching PeriodicDamageReductionComponent.HitsTaken's own reasoning.
    public int DamageInstancesTaken;
}
