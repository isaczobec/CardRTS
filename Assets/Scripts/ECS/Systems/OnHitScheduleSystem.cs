// Purely event-driven — no per-tick work of its own. In Setup, subscribes to DamageRequest's
// "executed" notification (mirrors OnKillScheduleSystem's own "react once a request has
// actually applied" shape, but on EVERY hit rather than only a killing one) and, once a hit
// is confirmed, checks whether the DEALER carries an OnHitScheduleComponent — if so, schedules
// its CallType via ScheduledCallSystem, DelayTicks ticks from now, passing the dealer's own
// entity id (Param0) and the entity it hit (Param3) — e.g. StalkerCard uses this to break its
// own Shadow Cloak and apply an on-hit debuff/buff the instant it lands a strike.
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

        OnHitScheduleComponent schedule = scheduleStore.GetComponent(dealerId);

        ScheduledCallSystem.Schedule(ecs, schedule.CallType, schedule.DelayTicks, dealerId, param3: request.EntityId);
    }
}
