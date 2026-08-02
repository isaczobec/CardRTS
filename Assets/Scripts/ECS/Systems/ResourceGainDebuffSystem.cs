// Subscribes to ResourcesAdded and scales Multiplier down for a player currently carrying
// an active ResourceGainDebuffComponent-carrying modifier (see DiscardCardSystem, the only
// source of this debuff today) — only for Wood/Stone/Metal/Gold; Gems/Soulstones gains are
// left untouched, matching the explicit "wood, metal, stone, and gold" design ask.
//
// Runs via a plain Subscribe (not SubscribeExecuted), same shape as ResourceDropBoostSystem/
// ComebackResourceBoostSystem — mutates Multiplier BEFORE ResourcesAdded.Execute applies
// Amount * Multiplier, so all three compose regardless of registration order (each just
// multiplies the same float). No isServer gate needed: this only scales a float on an
// in-flight request, safe to run identically on client prediction and server alike.
public static class ResourceGainDebuffSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
        => ecs.Requests.Subscribe<ResourcesAdded>(ApplyDebuff);

    private static void ApplyDebuff(ResourcesAdded request, ECS ecs)
    {
        if (request.Type != ResourceType.Wood && request.Type != ResourceType.Stone &&
            request.Type != ResourceType.Metal && request.Type != ResourceType.Gold)
            return;

        ulong modifierId = ModifierQuery.FindActiveModifierId<ResourceGainDebuffComponent>(ecs, request.PlayerEntityId);
        if (modifierId == 0) return;

        ComponentStore<ResourceGainDebuffComponent> debuffStore = ecs.GetComponentStore<ResourceGainDebuffComponent>();
        if (debuffStore == null) return;

        request.Multiplier *= debuffStore.GetComponent(modifierId).MultiplierRatio;
    }
}
