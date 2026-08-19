// Soft cap on Wood/Stone stockpiles — explicit design ask. Below SoftCapAmount of a
// resource, every gain of it applies in full. At or above it: passive income (see
// ResourcesAdded.IsPassive — ResourceGenerationSystem's own per-player *PerSecond baseline,
// ResourceGeneratorSystem's building procs) stops completely, while every one-time gain
// (resource-node drops, the trickle catch-up system, ...) is instead cut by
// OverCapPenaltyRatio rather than blocked outright — so a player sitting on a big stockpile
// can still creep upward by fighting/harvesting for it, just at a reduced rate, even though
// their passive income has capped out. Runs via a plain Subscribe (not SubscribeExecuted),
// same shape as ResourceGainDebuffSystem/ResourceDropBoostSystem — mutates Multiplier BEFORE
// ResourcesAdded.Execute applies Amount * Multiplier, so all of them compose regardless of
// registration order (each just multiplies the same float). No isServer gate needed: this
// only scales a float on an in-flight request, safe to run identically on client prediction
// and server alike.
public static class ResourceSoftCapSystem
{
    public const float SoftCapAmount = 500f;
    // -60% (explicit design ask) from the original -50%.
    private const float OverCapPenaltyRatio = 0.6f;

    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
        => ecs.Requests.Subscribe<ResourcesAdded>(ApplySoftCap);

    private static void ApplySoftCap(ResourcesAdded request, ECS ecs)
    {
        if (request.Type != ResourceType.Wood && request.Type != ResourceType.Stone) return;

        ComponentStore<PlayerResourcesComponent> store = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (store == null || !store.HasComponent(request.PlayerEntityId)) return;

        PlayerResourcesComponent resources = store.GetComponent(request.PlayerEntityId);
        float current = request.Type == ResourceType.Wood ? resources.Wood : resources.Stone;
        if (current < SoftCapAmount) return;

        request.Multiplier *= request.IsPassive ? 0f : (1f - OverCapPenaltyRatio);
    }
}
