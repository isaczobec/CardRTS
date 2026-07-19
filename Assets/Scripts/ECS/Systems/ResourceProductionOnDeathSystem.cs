// Purely event-driven — no per-tick work of its own. In Setup, subscribes to DeathRequest's
// "executed" notification (see RequestManager.SubscribeExecuted / DeathRequest.Execute) and,
// if the dying entity carries a ResourceProductionOnDeathComponent, permanently increases the
// passive production (PlayerResourcesComponent.*PerSecond) of whichever player owns
// HealthComponent.LastDamageDealer, by that component's own flat per-resource fields
// (converted from per-minute to per-second). Mirrors OnDeathResourceDropSystem's "read
// LastDamageDealer, resolve owner, credit them" shape.
//
// RespawnSystem only ever sets DeathRequest.ShouldDelete = false for a respawnable entity
// (Tree/Rock/Ore are all respawnable) — it does not cancel the request, so Execute (and this
// subscriber) still runs in full on every kill, not just the first. The boost is therefore
// uncapped and cumulative: repeatedly killing the same respawning node keeps compounding the
// killer's production forever, by design.
public static class ResourceProductionOnDeathSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private const float MinutesToSeconds = 60f;

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DeathRequest>(OnDeathExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDeathExecuted(DeathRequest request, ECS ecs)
    {
        ComponentStore<ResourceProductionOnDeathComponent> boostStore = ecs.GetComponentStore<ResourceProductionOnDeathComponent>();
        if (boostStore == null || !boostStore.HasComponent(request.EntityId)) return;

        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore == null || !healthStore.HasComponent(request.EntityId)) return;

        ulong dealerId = healthStore.GetComponent(request.EntityId).LastDamageDealer;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(dealerId)) return;

        ushort ownerPlayerId = troopStore.GetComponent(dealerId).OwnerPlayerId;
        if (ownerPlayerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return;

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, ownerPlayerId);
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId)) return;

        ResourceProductionOnDeathComponent boost = boostStore.GetComponent(request.EntityId);

        ref PlayerResourcesComponent resources = ref resourceStore.GetComponent(resourceEntityId);
        resources.WoodPerSecond       += boost.WoodPerMinute / MinutesToSeconds;
        resources.StonePerSecond      += boost.StonePerMinute / MinutesToSeconds;
        resources.MetalPerSecond      += boost.MetalPerMinute / MinutesToSeconds;
        resources.GemsPerSecond       += boost.GemsPerMinute / MinutesToSeconds;
        resources.SoulstonesPerSecond += boost.SoulstonesPerMinute / MinutesToSeconds;
        resources.GoldPerSecond       += boost.GoldPerMinute / MinutesToSeconds;
        ecs.Delta.MarkComponentDirty(resourceEntityId, typeof(PlayerResourcesComponent));
    }
}
