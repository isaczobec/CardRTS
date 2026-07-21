// Subscribes to CanMoveRequest/CanPerformRequest and vetoes both for any entity currently
// targeted by an active StunnedComponent modifier — mirrors ActionWindupSystem exactly, but
// for a genuine stun debuff (e.g. Frozen, see AbilityManager's Ice Nova ability) rather than
// a self-inflicted ability windup. Registered as a GlobalSystem purely for the Setup hook,
// same as ArmorMitigationSystem/ActionWindupSystem.
public static class StunnedSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<CanMoveRequest>((req, innerEcs) =>
        {
            if (IsStunned(innerEcs, req.EntityId))
                req.CanMove = false;
        });

        ecs.Requests.Subscribe<CanPerformRequest>((req, innerEcs) =>
        {
            if (IsStunned(innerEcs, req.EntityId))
                req.CanPerform = false;
        });
    }

    private static bool IsStunned(ECS ecs, ulong entityId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<StunnedComponent> stunnedStore = ecs.GetComponentStore<StunnedComponent>();
        if (modifierStore == null || stunnedStore == null) return false;

        bool stunned = false;
        modifierStore.ForEach((ulong modifierId) =>
        {
            if (stunned) return;
            if (!stunnedStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != entityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            stunned = true;
        });
        return stunned;
    }
}
