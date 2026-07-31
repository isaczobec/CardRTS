// Subscribes to CanPerformRequest and vetoes it for any entity currently targeted by an
// active SilenceComponent modifier — mirrors StunnedSystem/RootedSystem's own "walk every
// ModifierComponent targeting this entity" shape, but only vetoes CanPerformRequest (unlike
// StunnedSystem, which also vetoes CanMoveRequest/CanMoveOnOwnAccountRequest) — a silenced
// troop can still move under its own power, it just can't attack or use abilities. See
// SleepingDraughtCard/SilenceCard, the two cards that apply this. Registered as a
// GlobalSystem purely for the Setup hook, same as StunnedSystem/RootedSystem.
public static class SilenceSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<CanPerformRequest>((req, innerEcs) =>
        {
            if (IsSilenced(innerEcs, req.EntityId))
                req.CanPerform = false;
        });
    }

    private static bool IsSilenced(ECS ecs, ulong entityId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<SilenceComponent> silenceStore = ecs.GetComponentStore<SilenceComponent>();
        if (modifierStore == null || silenceStore == null) return false;

        bool silenced = false;
        modifierStore.ForEach((ulong modifierId) =>
        {
            if (silenced) return;
            if (!silenceStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != entityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            silenced = true;
        });
        return silenced;
    }
}
