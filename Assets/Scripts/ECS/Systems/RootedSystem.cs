// Subscribes to CanMoveOnOwnAccountRequest and vetoes it for any entity currently targeted
// by an active RootedComponent modifier — mirrors ActionWindupSystem/StunnedSystem's own
// "walk every ModifierComponent targeting this entity" shape, but only vetoes the narrower
// CanMoveOnOwnAccountRequest (like DisplacementSystem, not the broader CanMoveRequest/
// CanPerformRequest ActionWindupSystem/StunnedSystem also veto) — a rooted troop genuinely
// can still act and can still be displaced by an external push, it just can't walk under its
// own power. Registered as a GlobalSystem purely for the Setup hook, same as
// ActionWindupSystem/StunnedSystem.
public static class RootedSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<CanMoveOnOwnAccountRequest>((req, innerEcs) =>
        {
            if (IsRooted(innerEcs, req.EntityId))
                req.CanMoveOnOwnAccount = false;
        });
    }

    private static bool IsRooted(ECS ecs, ulong entityId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<RootedComponent> rootedStore = ecs.GetComponentStore<RootedComponent>();
        if (modifierStore == null || rootedStore == null) return false;

        bool rooted = false;
        modifierStore.ForEach((ulong modifierId) =>
        {
            if (rooted) return;
            if (!rootedStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != entityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            rooted = true;
        });
        return rooted;
    }
}
