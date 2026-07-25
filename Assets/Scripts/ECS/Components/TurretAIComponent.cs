// AI for a stationary defensive building (see CannonCard/TurretAISystem) — fires seeking
// projectiles at whichever enemy troop is currently closest and within Range, staying locked
// onto it across multiple attack cycles until it leaves Range, dies, or is otherwise
// invalidated, at which point the turret locks onto the next-closest qualifying enemy.
// Unlike BasicMeleeAIComponent/BasicRangedAIComponent, TargetEntityId is NEVER settable by
// player input — TurretAISystem never reads SetTargetsInput/TargetingSystem at all, so a
// turret can't be manually targeted the way a troop can.
public struct TurretAIComponent : IComponent
{
    public ulong TargetEntityId;

    // 0 = not currently winding up. Otherwise counts down AttackSpeed ticks before firing a
    // projectile at TargetEntityId.
    public int AttackTicksRemaining;

    // Post-shot wind-down — counted down before the turret will fire again. Same shape as
    // BasicRangedAIComponent.WindDownTicksRemaining/WindDownMultiplier.
    public int WindDownTicksRemaining;
    public float WindDownMultiplier;

    // Leeway (as a multiple of Range) allowed when re-checking TargetEntityId is still close
    // enough once the windup finishes, before actually firing — mirrors
    // BasicRangedAIComponent.AttackRangeMultiplier.
    public float AttackRangeMultiplier;
}
