public struct BasicRangedAIComponent : IComponent
{
    // How far (as a multiple of the troop's Range stat) it will notice and automatically
    // target nearby NEUTRAL entities (resource nodes) — see BasicRangedAISystem.AcquireTargets.
    public float DetectionRangeMultiplier;

    // Same, but for real ENEMY entities (troops/buildings) specifically — kept separate from
    // DetectionRangeMultiplier so a troop can be tuned to notice nearby resources from much
    // further away than it aggros onto enemies (Guard mode always prefers ANY tracked enemy
    // over ANY tracked neutral, regardless of which is actually closer — see
    // BasicRangedAISystem.ResolveGuardTarget — so a huge shared detection range let a troop
    // beeline for a distant enemy well past a much closer tree/rock it could've been
    // harvesting instead). 0 (the default, every card except SkillshotRangedTroopCard) falls
    // back to DetectionRangeMultiplier — set this explicitly only when a card wants a
    // genuinely different enemy-aggro range than its own resource-detection range.
    public float EnemyDetectionRangeMultiplier;

    // In Guard mode (AIModeComponent): how far (as a multiple of Range, larger than
    // DetectionRangeMultiplier) THIS TROOP may stray from its leash point while chasing an
    // automatically-acquired target before giving up on it — measured from the leash
    // position, not from the target. Unused in Aggressive mode, which never gives up an
    // automatic target at all; unused in Passive mode, which never acquires one.
    public float ChaseRangeMultiplier;

    // Leeway (as a multiple of Range) allowed when re-checking a target is still close
    // enough once the windup finishes, before actually firing a projectile at it.
    public float AttackRangeMultiplier;

    // 0 = not currently winding up. Otherwise the entity being wound up against.
    public ulong AttackTargetId;
    public int AttackTicksRemaining;

    // Post-shot wind-down: set (to the same length as the windup) after a projectile is
    // actually fired, counted down before this troop will act again (move, target, or
    // attack) — see BasicRangedAISystem.Tick.
    public int WindDownTicksRemaining;

    // Which entity this troop just fired at — 0 whenever WindDownTicksRemaining is 0. While
    // both are set, the troop can't start a new attack yet, but if this target has drifted
    // out of range in the meantime it still chases (without attacking) to keep pace, rather
    // than standing completely idle for the whole wind-down — see
    // BasicRangedAISystem.ChaseWhileWindingDown.
    public ulong WindDownTargetId;

    // Multiple of the attack windup (AttackSpeed ticks) that WindDownTicksRemaining is set
    // to once a shot fires — see BasicRangedAISystem.ResolveAttack. 3 reproduces every
    // existing troop's original hardcoded behavior; a card can set this lower to give a
    // proportionally shorter recovery relative to its (possibly slower) windup.
    public float WindDownMultiplier;

    // Target + position the last chase path was computed for. A moving target's
    // position changes almost every tick; repathing on every single one of those
    // changes is wasteful, so we only do it once the target has drifted more than the
    // troop's attack range from where it was the last time we computed a path to it.
    public ulong LastPathTargetId;
    public float LastPathTargetX;
    public float LastPathTargetY;
}
