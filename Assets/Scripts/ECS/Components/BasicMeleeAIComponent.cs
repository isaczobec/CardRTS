public struct BasicMeleeAIComponent : IComponent
{
    // "Leash" point the troop returns to once it has no targets left.
    public float OriginalX;
    public float OriginalY;

    // How far (as a multiple of the troop's Range stat) it will notice and
    // automatically target nearby enemies.
    public float DetectionRangeMultiplier;

    // How far (as a multiple of Range, larger than DetectionRangeMultiplier) it will
    // keep chasing an automatically-acquired target before giving up on it.
    public float ChaseRangeMultiplier;

    // Leeway (as a multiple of Range) allowed when re-checking a target is still close
    // enough once an attack's windup finishes.
    public float AttackRangeMultiplier;

    // 0 = not currently mid-attack. Otherwise the entity being wound up against.
    public ulong AttackTargetId;
    public int AttackTicksRemaining;

    // Post-attack recovery: set after a successful hit lands, counted down before this
    // troop will act again (move, target, or attack) — see BasicMeleeAISystem.Tick.
    public int CooldownTicksRemaining;

    // Target + position the last chase path was computed for. A moving target's
    // position changes almost every tick; repathing on every single one of those
    // changes is wasteful, so we only do it once the target has drifted more than the
    // troop's attack range from where it was the last time we computed a path to it.
    public ulong LastPathTargetId;
    public float LastPathTargetX;
    public float LastPathTargetY;
}
