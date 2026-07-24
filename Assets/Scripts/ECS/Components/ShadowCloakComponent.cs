// Payload for a ModifierComponent-carrying modifier entity — while active, makes its
// target (ModifierComponent.TargetEntityId) untargetable by anyone other than its own
// owner (see ShadowCloakSystem/AbilityManager's Shadow Cloak ability, StalkerCard). Same
// shape as StunnedComponent: no fields, its mere presence on an active modifier is the
// whole effect. Deliberately does NOT stop an already-fired projectile (seeking or
// hitbox) from landing — SeekingProjectileSystem/SkillshotProjectileSystem never consult
// this, only future target ACQUISITION does (see ShadowCloakSystem's own doc comment).
public struct ShadowCloakComponent : IComponent
{
}
