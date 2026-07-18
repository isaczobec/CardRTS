using System.Collections.Generic;

// Reads SetAIModeInput each tick: for every entity id in it that has an AIModeComponent, is
// owned by the requesting client, and can currently take actions, sets its mode. Pure
// component mutation with no entity creation — safe to run unconditionally (predicted on
// both client and server, no isServer guard), mirroring TargetingSystem's own reasoning for
// SetTargetsInput. Entities without AIModeComponent (buildings) are silently skipped rather
// than rejected outright, since a selection can freely mix troops and buildings.
public static class SetAIModeSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<SetAIModeInput> inputs = ecs.GetInputsForTick<SetAIModeInput>();
        if (inputs == null || inputs.Count == 0) return;

        ComponentStore<AIModeComponent> aiModeStore = ecs.GetComponentStore<AIModeComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();

        foreach (SetAIModeInput input in inputs)
            foreach (ulong entityId in input.EntityIds)
                SetMode(ecs, input, entityId, aiModeStore, troopStore);
    }

    private static void SetMode(ECS ecs, SetAIModeInput input, ulong entityId,
        ComponentStore<AIModeComponent> aiModeStore, ComponentStore<TroopComponent> troopStore)
    {
        if (!aiModeStore.HasComponent(entityId)) return;
        if (!troopStore.HasComponent(entityId)) return;
        if (troopStore.GetComponent(entityId).OwnerPlayerId != input.ClientId) return;
        if (!ActivationQuery.CanTakeActions(ecs, entityId)) return;

        ref AIModeComponent aiMode = ref aiModeStore.GetComponent(entityId);
        if (aiMode.Mode == input.Mode) return;

        aiMode.Mode = input.Mode;
        ecs.Delta.MarkComponentDirty(entityId, typeof(AIModeComponent));
    }
}
