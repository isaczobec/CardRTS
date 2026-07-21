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
        ComponentStore<BasicMeleeAIComponent> meleeAiStore = ecs.GetComponentStore<BasicMeleeAIComponent>();
        ComponentStore<BasicRangedAIComponent> rangedAiStore = ecs.GetComponentStore<BasicRangedAIComponent>();
        TargetingSystem targeting = ecs.GetSystem<TargetingSystem>();

        foreach (SetAIModeInput input in inputs)
            foreach (ulong entityId in input.EntityIds)
                SetMode(ecs, input, entityId, aiModeStore, troopStore, meleeAiStore, rangedAiStore, targeting);
    }

    private static void SetMode(ECS ecs, SetAIModeInput input, ulong entityId,
        ComponentStore<AIModeComponent> aiModeStore, ComponentStore<TroopComponent> troopStore,
        ComponentStore<BasicMeleeAIComponent> meleeAiStore, ComponentStore<BasicRangedAIComponent> rangedAiStore,
        TargetingSystem targeting)
    {
        if (!aiModeStore.HasComponent(entityId)) return;
        if (!troopStore.HasComponent(entityId)) return;
        if (troopStore.GetComponent(entityId).OwnerPlayerId != input.ClientId) return;
        if (!ActivationQuery.IsActivated(ecs, entityId)) return;

        ref AIModeComponent aiMode = ref aiModeStore.GetComponent(entityId);
        if (aiMode.Mode == input.Mode) return;

        aiMode.Mode = input.Mode;
        ecs.Delta.MarkComponentDirty(entityId, typeof(AIModeComponent));

        // Passive never picks its own targets (AcquireTargets and the automatic-target
        // fallback are both skipped entirely for Passive in BasicMeleeAISystem/
        // BasicRangedAISystem), but switching into it doesn't retroactively forget one
        // already picked up under Guard/Aggressive — without dropping it here too, the troop
        // would keep moving toward it until it drifted out of detection/leash range on its
        // own. Clearing _targets' automatic entries alone isn't enough, though: LastPathTargetId
        // (replicated component data BasicMeleeAISystem/BasicRangedAISystem falls back to when
        // _targets has no record at all — see Tick's activeTarget resolution) would otherwise
        // still resurrect the chase next tick, so it's cleared here too, unless it's currently
        // a player-assigned target (an explicit order is always honored regardless of mode).
        if (input.Mode == AIMode.Passive)
        {
            targeting?.ClearAutomaticTargets(entityId);
            ClearStaleLastPathTarget(ecs, entityId, targeting, meleeAiStore, rangedAiStore);
        }
    }

    private static void ClearStaleLastPathTarget(ECS ecs, ulong entityId, TargetingSystem targeting,
        ComponentStore<BasicMeleeAIComponent> meleeAiStore, ComponentStore<BasicRangedAIComponent> rangedAiStore)
    {
        if (meleeAiStore != null && meleeAiStore.HasComponent(entityId))
        {
            ref BasicMeleeAIComponent ai = ref meleeAiStore.GetComponent(entityId);
            if (ai.LastPathTargetId != 0 && targeting?.GetTargetKind(entityId, ai.LastPathTargetId) != TargetKind.PlayerAssigned)
            {
                ai.LastPathTargetId = 0;
                ecs.Delta.MarkComponentDirty(entityId, typeof(BasicMeleeAIComponent));
            }
        }

        if (rangedAiStore != null && rangedAiStore.HasComponent(entityId))
        {
            ref BasicRangedAIComponent ai = ref rangedAiStore.GetComponent(entityId);
            if (ai.LastPathTargetId != 0 && targeting?.GetTargetKind(entityId, ai.LastPathTargetId) != TargetKind.PlayerAssigned)
            {
                ai.LastPathTargetId = 0;
                ecs.Delta.MarkComponentDirty(entityId, typeof(BasicRangedAIComponent));
            }
        }
    }
}
