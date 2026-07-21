// Applies every ProjectileHitRequest queued this tick (SeekingProjectileSystem/
// SkillshotProjectileSystem). Must run before DamageResolutionSystem: ProjectileHitRequest.
// Execute enqueues a DamageRequest rather than dealing damage itself, so that still needs
// its own flush afterward, same tick.
public static class ProjectileHitResolutionSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ecs.Requests.Flush<ProjectileHitRequest>(ecs);
    }
}
