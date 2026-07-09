// Applies every DamageRequest queued this tick (e.g. by BasicMeleeAISystem) against its
// target's HealthComponent. Runs before DeathSystem so a lethal hit is reflected in the
// same tick's death check.
public static class DamageResolutionSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ecs.Requests.Flush<DamageRequest>(ecs);
    }
}
