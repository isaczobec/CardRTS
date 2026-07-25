// Purely event-driven — no per-tick work of its own. In Setup, subscribes to DamageRequest's
// "executed" notification (mirrors OnKillScheduleSystem's own "react once a request has
// actually applied" shape, but on every QUALIFYING hit rather than only a killing one) and,
// once a hit is confirmed, checks whether the DEALER carries an OnHitScheduleComponent — if
// so (and the hit qualifies — see RequireEnemyTroopHit), counts it against PeriodHits/
// HitsUntilProc and, once that countdown reaches 0, schedules CallType via
// ScheduledCallSystem, DelayTicks ticks from now, passing the dealer's own entity id
// (Param0) and the entity it hit (Param3) — e.g. StalkerCard uses this (at the default
// PeriodHits — procs every hit) to break its own Shadow Cloak and apply an on-hit debuff/
// buff the instant it lands a strike; SantaClausCard uses PeriodHits = 8 to spawn a
// reinforcement only every 8th qualifying hit.
using UnityEngine;

public static class OnHitScheduleSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<OnHitScheduleComponent> scheduleStore = ecs.GetComponentStore<OnHitScheduleComponent>();
        if (scheduleStore == null || !scheduleStore.HasComponent(dealerId)) return;

        ref OnHitScheduleComponent schedule = ref scheduleStore.GetComponent(dealerId);

        if (schedule.RequireEnemyTroopHit && !IsEnemyTroopHit(ecs, dealerId, request.EntityId)) return;
        if (schedule.RequireEnemyOwnedHit && !IsEnemyOwnedHit(ecs, dealerId, request.EntityId)) return;

        schedule.HitsUntilProc--;
        if (schedule.HitsUntilProc > 0)
        {
            ecs.Delta.MarkComponentDirty(dealerId, typeof(OnHitScheduleComponent));
            return;
        }

        schedule.HitsUntilProc = Mathf.Max(1, schedule.PeriodHits);
        ecs.Delta.MarkComponentDirty(dealerId, typeof(OnHitScheduleComponent));

        ScheduledCallSystem.Schedule(ecs, schedule.CallType, schedule.DelayTicks, dealerId, param3: request.EntityId);
    }

    private static bool IsEnemyTroopHit(ECS ecs, ulong dealerId, ulong targetId)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null) return false;
        if (!troopStore.HasComponent(dealerId) || !troopStore.HasComponent(targetId)) return false;

        TroopComponent target = troopStore.GetComponent(targetId);
        if (!target.IsPhysicalTroop) return false;

        return target.OwnerPlayerId != troopStore.GetComponent(dealerId).OwnerPlayerId;
    }

    // Unlike IsEnemyTroopHit, doesn't require the target to be a physical troop — a hit
    // against an enemy BUILDING counts too. Only excludes a NEUTRAL-owned target (e.g. a
    // resource node, or a neutral building — see TurretAISystem's own neutral-owner check).
    private static bool IsEnemyOwnedHit(ECS ecs, ulong dealerId, ulong targetId)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null) return false;
        if (!troopStore.HasComponent(dealerId) || !troopStore.HasComponent(targetId)) return false;

        ushort targetOwnerId = troopStore.GetComponent(targetId).OwnerPlayerId;
        if (targetOwnerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return false;

        return targetOwnerId != troopStore.GetComponent(dealerId).OwnerPlayerId;
    }
}
