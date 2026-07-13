// Purely event-driven — no per-tick work of its own. In Setup, subscribes to DeathRequest's
// "executed" notification (see RequestManager.SubscribeExecuted / DeathRequest.Execute,
// which fires this before deleting the entity) and, if the dying entity carries an
// OnDeathResourceDropComponent, credits its Drop to whichever player owns
// HealthComponent.LastDamageDealer.
//
// Only fires for deaths that actually executed — RespawnSystem cancels DeathRequest for
// respawnable entities, so those never reach here.
public static class OnDeathResourceDropSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DeathRequest>(OnDeathExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDeathExecuted(DeathRequest request, ECS ecs)
    {
        ComponentStore<OnDeathResourceDropComponent> dropStore = ecs.GetComponentStore<OnDeathResourceDropComponent>();
        if (dropStore == null || !dropStore.HasComponent(request.EntityId)) return;

        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore == null || !healthStore.HasComponent(request.EntityId)) return;

        ulong dealerId = healthStore.GetComponent(request.EntityId).LastDamageDealer;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(dealerId)) return;

        ushort ownerPlayerId = troopStore.GetComponent(dealerId).OwnerPlayerId;
        if (ownerPlayerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return;

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, ownerPlayerId);
        if (resourceEntityId == 0) return;

        ResourceCost drop = dropStore.GetComponent(request.EntityId).Drop;
        GrantResource(ecs, resourceEntityId, ResourceType.Wood, drop.Wood);
        GrantResource(ecs, resourceEntityId, ResourceType.Stone, drop.Stone);
        GrantResource(ecs, resourceEntityId, ResourceType.Metal, drop.Metal);
        GrantResource(ecs, resourceEntityId, ResourceType.Gems, drop.Gems);
        GrantResource(ecs, resourceEntityId, ResourceType.Soulstones, drop.Soulstones);
        GrantResource(ecs, resourceEntityId, ResourceType.Gold, drop.Gold);
    }

    // Queues onto this ECS's own RequestManager — ResourceGenerationSystem (registered
    // last in the tick) flushes all pending ResourcesAdded requests, so this doesn't need
    // to flush them itself.
    private static void GrantResource(ECS ecs, ulong resourceEntityId, ResourceType type, int amount)
    {
        if (amount == 0) return;
        ecs.Requests.CreateRequest(new ResourcesAdded(resourceEntityId, type, amount));
    }
}
