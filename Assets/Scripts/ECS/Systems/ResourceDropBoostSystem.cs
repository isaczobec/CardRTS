// Subscribes to ResourcesAdded and scales Multiplier up by (1 + BoostRatio) for any
// positional drop (ResourcesAdded.X/Y set — e.g. a harvested tree/rock/ore, or a troop's
// on-death gold drop; passive per-tick generation uses NO_WORLD_LOCATION and is never
// affected) whose position falls within RangeMultiplier x a nearby active
// ResourceDropBoostComponent-carrying troop's own Range stat, owned by the same player
// receiving the resources. Only the first qualifying troop found applies its boost —
// explicit "should not stack" design ask, so several Strategy Consultants near the same
// drop don't compound the bonus. See StrategyConsultantCard, the only card that grants this
// today.
//
// Runs via a plain Subscribe (not SubscribeExecuted) so it mutates Multiplier BEFORE
// ResourcesAdded.Execute applies Amount * Multiplier — same shape as every other
// request-mutating system (ArmorMitigationSystem, BuildingDamageBonusSystem, ...). No
// isServer gate needed: this only scales a float on an in-flight request, safe to run
// identically on client prediction and server alike.
public static class ResourceDropBoostSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
        => ecs.Requests.Subscribe<ResourcesAdded>(TryBoost);

    private static void TryBoost(ResourcesAdded request, ECS ecs)
    {
        if (request.X == ResourcesAdded.NO_WORLD_LOCATION || request.Y == ResourcesAdded.NO_WORLD_LOCATION) return;

        ComponentStore<ResourceDropBoostComponent> boostStore = ecs.GetComponentStore<ResourceDropBoostComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (boostStore == null || troopStore == null || posStore == null) return;

        ushort ownerPlayerId = ResourceHelper.GetOwnerPlayerId(ecs, request.PlayerEntityId);

        bool boosted = false;
        boostStore.ForEach((ulong troopId) =>
        {
            if (boosted) return;
            if (!troopStore.HasComponent(troopId)) return;

            TroopComponent troop = troopStore.GetComponent(troopId);
            if (troop.OwnerPlayerId != ownerPlayerId) return;
            if (troop.IsDead) return;
            if (!posStore.HasComponent(troopId)) return;
            if (!ActivationQuery.IsActivated(ecs, troopId)) return;

            PositionComponent pos = posStore.GetComponent(troopId);
            ResourceDropBoostComponent boost = boostStore.GetComponent(troopId);
            float range = StatsQuery.GetRange(ecs, troopId, 5) * boost.RangeMultiplier;
            float dx = pos.X - request.X;
            float dy = pos.Y - request.Y;
            if (dx * dx + dy * dy > range * range) return;

            request.Multiplier *= 1f + boost.BoostRatio;
            boosted = true;
        });
    }
}
