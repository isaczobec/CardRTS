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
        ecs.FlagEvents.Add(new ResourcesChangedEvent { EntityId = resourceEntityId });
    }
}
