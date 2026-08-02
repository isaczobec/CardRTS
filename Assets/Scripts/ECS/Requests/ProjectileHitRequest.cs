// Represents a pooled projectile (SeekingProjectileSystem/SkillshotProjectileSystem)
// actually hitting a target. Execute just enqueues the resulting DamageRequest (flushed
// afterward, same tick, by DamageResolutionSystem — see ProjectileHitResolutionSystem for
// where THIS request itself gets flushed, ahead of that) rather than dealing damage
// directly, so anything already subscribed to DamageRequest (ArmorMitigationSystem,
// PeriodicDamageReductionSystem, ...) still runs exactly as it does for every other damage
// source. ProjectileOnHitSystem separately subscribes to this request's "executed"
// callback (SubscribeExecuted) to apply whatever on-hit effect the projectile carries (see
// ProjectileOnHitComponent) once the hit is confirmed to have actually happened, rather
// than reacting to the damage itself.
public class ProjectileHitRequest : Request
{
    public readonly ulong ProjectileId;
    public readonly ulong OwnerId;
    public readonly ulong TargetId;
    public readonly int Damage;

    // What kind of damage instance the resulting DamageRequest should be tagged as (see
    // DamageProcType) — Direct by default, so every existing call site that doesn't set this
    // keeps behaving exactly as before ProcType existed.
    public DamageProcType ProcType = DamageProcType.Direct;

    public ProjectileHitRequest(ulong projectileId, ulong ownerId, ulong targetId, int damage)
    {
        ProjectileId = projectileId;
        OwnerId = ownerId;
        TargetId = targetId;
        Damage = damage;
    }

    public override void Execute(ECS ecs)
    {
        ecs.Requests.CreateRequest(new DamageRequest(TargetId, Damage) { DealerEntityId = OwnerId, ProcType = ProcType });
    }
}
