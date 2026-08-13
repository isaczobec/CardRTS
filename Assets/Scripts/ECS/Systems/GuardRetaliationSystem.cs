// Purely event-driven, mirrors RemoveModifierOnDamageSystem/OnHitScheduleSystem's own shape —
// no per-tick work of its own, just a DamageRequest "executed" subscription. Guard mode
// (AIModeComponent) normally never touches a player-assigned target (see BasicMeleeAISystem/
// BasicRangedAISystem's ResolveGuardTarget, which only ever picks from AUTOMATIC targets — a
// player-assigned one always wins outright and is never reconsidered). This is the one
// exception: the instant a Guard-mode troop actually takes a hit from a real enemy, it drops
// whatever it was manually told to do (ClearPlayerAssignedTargets) and starts fighting back —
// tagging the attacker itself as an automatic target so it's immediately eligible to win
// BasicMeleeAISystem/BasicRangedAISystem's enemy-over-neutral tiering that same tick, rather
// than waiting for the attacker to wander within normal detection range on some later tick (it
// may already be outside detection range — e.g. a ranged attacker sniping from just past it).
//
// Aggressive mode doesn't need this: it already always prefers any nearby enemy over a neutral
// target (including overriding a manual neutral order) on its own — see
// BasicMeleeAISystem.ResolveAggressiveTarget. Passive is untouched — it never acts on its own
// under any circumstance, only in direct response to explicit player input.
public static class GuardRetaliationSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        if (request.Amount <= 0) return; // fully absorbed/mitigated — not "taking damage"

        ulong victimId = request.EntityId;
        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID || dealerId == victimId) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(victimId) || !troopStore.HasComponent(dealerId)) return;

        TroopComponent victim = troopStore.GetComponent(victimId);
        if (!victim.IsPhysicalTroop) return; // only a real troop has a mode/chase behavior to retaliate with

        TroopComponent dealer = troopStore.GetComponent(dealerId);
        if (dealer.OwnerPlayerId == victim.OwnerPlayerId) return; // friendly fire doesn't provoke retaliation
        if (dealer.OwnerPlayerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return; // nor a neutral damage source

        ComponentStore<AIModeComponent> aiModeStore = ecs.GetComponentStore<AIModeComponent>();
        AIMode mode = aiModeStore != null && aiModeStore.HasComponent(victimId) ? aiModeStore.GetComponent(victimId).Mode : AIMode.Guard;
        if (mode != AIMode.Guard) return;

        TargetingSystem targeting = ecs.GetSystem<TargetingSystem>();
        if (targeting == null) return;

        targeting.ClearPlayerAssignedTargets(victimId);
        targeting.SetAutomaticTarget(victimId, dealerId);
    }
}
