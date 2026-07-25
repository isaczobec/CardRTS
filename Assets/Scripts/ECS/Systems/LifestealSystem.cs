// Purely event-driven — no per-tick work of its own. Subscribes to DamageRequest's "executed"
// notification (mirrors OnHitScheduleSystem's own shape) and, once a hit is confirmed, visits
// EVERY active LifestealComponent-carrying modifier targeting the DEALER (see
// ModifierQuery.ForEachActiveModifierId — buying the Lifesteal upgrade more than once on the
// same card creates a separate modifier entity per purchase, see CardUpgrade/UpgradeQuery, so
// each one heals independently rather than only the first found) and, for each, heals the
// dealer via its own new HealRequest for LifestealRatio x the (already fully armor-mitigated —
// SubscribeExecuted fires after ArmorMitigationSystem/BarrierSystem's own Subscribe callbacks
// have already adjusted request.Amount, see DamageRequest) damage that was actually dealt.
// Uses HealRequest's flat Additive field (not Ratio, which is relative to the HEALED entity's
// own max health) since lifesteal is based on damage dealt, not the healer's max health —
// explicit design ask ("create a heal request on every damage request from this [troop] to
// implement"). Only NeutralTargetEffectivenessRatio of that heal applies when the target hit
// is neutral-owned (e.g. a resource node) — explicit design ask, discouraging farming neutral
// targets purely for sustain.
public static class LifestealSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private const float NeutralTargetEffectivenessRatio = 0.10f;

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        bool isNeutralTarget = IsNeutralTarget(ecs, request.EntityId);

        ModifierQuery.ForEachActiveModifierId<LifestealComponent>(ecs, dealerId, modifierId =>
        {
            ComponentStore<LifestealComponent> store = ecs.GetComponentStore<LifestealComponent>();
            float lifestealRatio = store.GetComponent(modifierId).LifestealRatio;
            if (isNeutralTarget)
                lifestealRatio *= NeutralTargetEffectivenessRatio;

            float healAmount = request.Amount * lifestealRatio;
            if (healAmount <= 0f) return;

            ecs.Requests.CreateRequest(new HealRequest(dealerId, healAmount));
        });
    }

    private static bool IsNeutralTarget(ECS ecs, ulong targetId)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(targetId)) return false;

        return troopStore.GetComponent(targetId).OwnerPlayerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID;
    }
}
