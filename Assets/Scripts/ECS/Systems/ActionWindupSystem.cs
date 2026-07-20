// Subscribes to CanMoveRequest/CanPerformRequest and vetoes both for any entity currently
// targeted by an active ActionWindupComponent modifier — mirrors StatModifierSystem's own
// "walk every ModifierComponent targeting this entity" shape, but as a straight veto
// instead of accumulating a bonus. Registered as a GlobalSystem purely for the Setup hook,
// same as ArmorMitigationSystem/StatModifierSystem.
public static class ActionWindupSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<CanMoveRequest>((req, innerEcs) =>
        {
            if (IsWindingUp(innerEcs, req.EntityId))
                req.CanMove = false;
        });

        ecs.Requests.Subscribe<CanPerformRequest>((req, innerEcs) =>
        {
            if (IsWindingUp(innerEcs, req.EntityId))
                req.CanPerform = false;
        });
    }

    private static bool IsWindingUp(ECS ecs, ulong entityId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<ActionWindupComponent> windupStore = ecs.GetComponentStore<ActionWindupComponent>();
        if (modifierStore == null || windupStore == null) return false;

        bool windingUp = false;
        modifierStore.ForEach((ulong modifierId) =>
        {
            if (windingUp) return;
            if (!windupStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != entityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            windingUp = true;
        });
        return windingUp;
    }
}
