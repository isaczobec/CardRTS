using UnityEngine;

// Purely event-driven — no per-tick work of its own. Subscribes to DamageRequest's "executed"
// notification (mirrors OnHitScheduleSystem's own shape) and, once a hit is confirmed, looks
// up whether the DEALER has an active FocusFireComponent-carrying modifier (see
// ModifierQuery.FindActiveModifierId — FocusFireUpgrade.MaxStackCount is 1, so at most one
// ever exists per card) — if so, either increments its Stacks (capped at MaxStacks) when the
// hit landed on the SAME target as last time, or resets to a fresh single stack against the
// new target otherwise (explicit design ask: "dealing damage to any other target resets the
// stacks"). The resulting stack count is then folded straight into that SAME modifier
// entity's own StatModifierComponent.AttackSpeedRatioBonus — one entity carries both the
// bookkeeping (FocusFireComponent) and the real effect (StatModifierComponent, read by
// StatModifierSystem regardless of ModifierID) and its icon (ModifierID.FocusFire — see
// ModifierIconManager.ResolveFocusFire), rather than three separate pieces.
public static class FocusFireSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ulong modifierId = ModifierQuery.FindActiveModifierId<FocusFireComponent>(ecs, dealerId);
        if (modifierId == 0) return;

        ComponentStore<FocusFireComponent> store = ecs.GetComponentStore<FocusFireComponent>();
        ref FocusFireComponent focus = ref store.GetComponent(modifierId);

        if (focus.CurrentTargetId == request.EntityId)
        {
            focus.Stacks = Mathf.Min(focus.Stacks + 1, focus.MaxStacks);
        }
        else
        {
            focus.CurrentTargetId = request.EntityId;
            focus.Stacks = 1;
        }
        ecs.Delta.MarkComponentDirty(modifierId, typeof(FocusFireComponent));

        ComponentStore<StatModifierComponent> statStore = ecs.GetComponentStore<StatModifierComponent>();
        ref StatModifierComponent stat = ref statStore.GetComponent(modifierId);
        stat.AttackSpeedRatioBonus = -focus.AttackSpeedRatioBonusPerStack * focus.Stacks;
        ecs.Delta.MarkComponentDirty(modifierId, typeof(StatModifierComponent));
    }
}
