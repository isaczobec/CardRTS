// Shared per-player resource lookup/mutation, mirroring DeckHelper.FindPlayerDeckEntity's
// pattern for PlayerResourcesComponent.
public static class ResourceHelper
{
    // Finds the entity carrying ownerPlayerId's PlayerResourcesComponent, or 0 if none
    // exists (e.g. that player's entity hasn't been set up yet).
    public static ulong FindPlayerResourcesEntity(ECS ecs, ushort ownerPlayerId)
    {
        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (playerStore == null || resourceStore == null) return 0;

        ulong found = 0;
        playerStore.ForEach((ulong id) =>
        {
            if (found != 0) return;
            if (!resourceStore.HasComponent(id)) return;
            if (playerStore.GetComponent(id).PlayerId == ownerPlayerId) found = id;
        });
        return found;
    }

    // Reads PlayerComponent.PlayerId directly off resourceEntityId — the inverse of
    // FindPlayerResourcesEntity, and O(1) since PlayerComponent and PlayerResourcesComponent
    // live on the same entity (see NetworkManager.SpawnPlayerEntity).
    public static ushort GetOwnerPlayerId(ECS ecs, ulong resourceEntityId)
    {
        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        if (playerStore == null || !playerStore.HasComponent(resourceEntityId)) return 0;
        return playerStore.GetComponent(resourceEntityId).PlayerId;
    }

    // Subtracts cost from resourceEntityId's PlayerResourcesComponent — the caller is
    // expected to have already checked ResourceCost.CanAfford — and raises
    // ResourcesChangedEvent so clients can re-evaluate affordability (e.g. auto-deselecting
    // a now-unaffordable selected card).
    public static void Spend(ECS ecs, ulong resourceEntityId, ResourceCost cost)
    {
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || !resourceStore.HasComponent(resourceEntityId)) return;

        ref PlayerResourcesComponent resources = ref resourceStore.GetComponent(resourceEntityId);
        resources.Wood       -= cost.Wood;
        resources.Stone      -= cost.Stone;
        resources.Metal      -= cost.Metal;
        resources.Gems       -= cost.Gems;
        resources.Soulstones -= cost.Soulstones;
        resources.Gold       -= cost.Gold;

        ecs.Delta.MarkComponentDirty(resourceEntityId, typeof(PlayerResourcesComponent));
        ecs.FlagEvents.Add(new ResourcesChangedEvent
        {
            EntityId = resourceEntityId,
            ClientId = GetOwnerPlayerId(ecs, resourceEntityId),
            WoodDelta       = -cost.Wood,
            StoneDelta      = -cost.Stone,
            MetalDelta      = -cost.Metal,
            GemsDelta       = -cost.Gems,
            SoulstonesDelta = -cost.Soulstones,
            GoldDelta       = -cost.Gold,
            // X/Y left at NO_WORLD_LOCATION — spending isn't tied to a spot in the world.
        });
    }
}
