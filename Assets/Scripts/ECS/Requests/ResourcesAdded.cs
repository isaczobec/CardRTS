// Adds Amount * Multiplier of Type to PlayerEntityId's PlayerResourcesComponent when
// executed. Amount and Multiplier are both mutable (unlike DamageRequest's readonly
// EntityId) so a Subscribe callback can adjust either — e.g. a production-boost buff
// scaling Multiplier up, or a resource-cap listener clamping Amount down — before Execute
// applies the product.
public class ResourcesAdded : Request
{
    public readonly ulong PlayerEntityId;
    public readonly ResourceType Type;
    public float Amount;
    public float Multiplier;

    public ResourcesAdded(ulong playerEntityId, ResourceType type, float amount, float multiplier = 1f)
    {
        PlayerEntityId = playerEntityId;
        Type = type;
        Amount = amount;
        Multiplier = multiplier;
    }

    public override void Execute(ECS ecs)
    {
        ComponentStore<PlayerResourcesComponent> store = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (store == null || !store.HasComponent(PlayerEntityId)) return;

        float total = Amount * Multiplier;
        ref PlayerResourcesComponent resources = ref store.GetComponent(PlayerEntityId);

        switch (Type)
        {
            case ResourceType.Wood:       resources.Wood       += total; break;
            case ResourceType.Stone:      resources.Stone      += total; break;
            case ResourceType.Metal:      resources.Metal      += total; break;
            case ResourceType.Gems:       resources.Gems       += total; break;
            case ResourceType.Soulstones: resources.Soulstones += total; break;
            case ResourceType.Gold:       resources.Gold       += total; break;
        }

        ecs.Delta.MarkComponentDirty(PlayerEntityId, typeof(PlayerResourcesComponent));
        ecs.FlagEvents.Add(new ResourcesChangedEvent { EntityId = PlayerEntityId });
    }
}
