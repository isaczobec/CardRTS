// Runs late in the tick (after every other system that might queue a ResourcesAdded
// request, e.g. a future "sell resource" or "production building" system). Queues one
// ResourcesAdded request per player per resource for this tick's share of their passive
// *PerSecond generation, then flushes every pending ResourcesAdded request so they're all
// applied before the next tick reads PlayerResourcesComponent.
public static class ResourceGenerationSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore != null)
        {
            resourceStore.ForEach((ulong id) =>
            {
                PlayerResourcesComponent resources = resourceStore.GetComponent(id);
                QueueGeneration(ecs, id, ResourceType.Wood, resources.WoodPerSecond);
                QueueGeneration(ecs, id, ResourceType.Stone, resources.StonePerSecond);
                QueueGeneration(ecs, id, ResourceType.Metal, resources.MetalPerSecond);
                QueueGeneration(ecs, id, ResourceType.Gems, resources.GemsPerSecond);
                QueueGeneration(ecs, id, ResourceType.Soulstones, resources.SoulstonesPerSecond);
                QueueGeneration(ecs, id, ResourceType.Gold, resources.GoldPerSecond);
            });
        }

        ecs.Requests.Flush<ResourcesAdded>(ecs);
    }

    private static void QueueGeneration(ECS ecs, ulong playerEntityId, ResourceType type, float perSecond)
    {
        if (perSecond == 0f) return;
        ecs.Requests.CreateRequest(new ResourcesAdded(playerEntityId, type, perSecond * TickManager.TickInterval));
    }
}
