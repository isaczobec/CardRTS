// Applies every HealRequest queued this tick — mirrors DamageResolutionSystem exactly, just
// for healing instead of damage.
public static class HealResolutionSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ecs.Requests.Flush<HealRequest>(ecs);
    }
}
