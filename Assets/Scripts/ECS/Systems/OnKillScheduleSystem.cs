// Purely event-driven — no per-tick work of its own. In Setup, subscribes to DeathRequest's
// "executed" notification (mirrors OnDeathResourceDropSystem/ResourceProductionOnDeathSystem's
// own "read HealthComponent.LastDamageDealer, resolve the killer" shape) and, once a kill is
// confirmed, checks whether the KILLER carries an OnKillScheduleComponent — if so, schedules
// its CallType via ScheduledCallSystem, DelayTicks ticks from now, passing the killer's own
// entity id (Param0) and the position the kill happened at (Param1/Param2) — e.g.
// SkeletonsCard uses this to raise a fresh friendly skeleton where an enemy troop died.
//
// A "kill" here means: this troop's HealthComponent.LastDamageDealer (set by DamageRequest.
// Execute) is a troop carrying OnKillScheduleComponent, and the victim is either a real
// troop or (only if OnKillScheduleComponent.AllowBuildingKills is true) a building.
public static class OnKillScheduleSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DeathRequest>(OnDeathExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDeathExecuted(DeathRequest request, ECS ecs)
    {
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore == null || !healthStore.HasComponent(request.EntityId)) return;

        ulong killerId = healthStore.GetComponent(request.EntityId).LastDamageDealer;
        if (killerId == 0 || killerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<OnKillScheduleComponent> scheduleStore = ecs.GetComponentStore<OnKillScheduleComponent>();
        if (scheduleStore == null || !scheduleStore.HasComponent(killerId)) return;

        OnKillScheduleComponent schedule = scheduleStore.GetComponent(killerId);

        ComponentStore<BuildingComponent> buildingStore = ecs.GetComponentStore<BuildingComponent>();
        bool victimIsBuilding = buildingStore != null && buildingStore.HasComponent(request.EntityId);
        if (victimIsBuilding && !schedule.AllowBuildingKills) return;

        float killX = 0f;
        float killY = 0f;
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore != null && posStore.HasComponent(request.EntityId))
        {
            PositionComponent pos = posStore.GetComponent(request.EntityId);
            killX = pos.X;
            killY = pos.Y;
        }

        ScheduledCallSystem.Schedule(ecs, schedule.CallType, schedule.DelayTicks, killerId, killX, killY);
    }
}
