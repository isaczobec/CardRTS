// Adds Amount * Multiplier of Type to PlayerEntityId's PlayerResourcesComponent when
// executed. Amount and Multiplier are both mutable (unlike DamageRequest's readonly
// EntityId) so a Subscribe callback can adjust either — e.g. a production-boost buff
// scaling Multiplier up, or a resource-cap listener clamping Amount down — before Execute
// applies the product.
public class ResourcesAdded : Request
{
    // Sentinel for "this gain isn't tied to any particular spot in the world" (e.g.
    // passive per-tick generation) — as opposed to X/Y of (0, 0), a real location. Read by
    // FloatingTextManager to decide whether a gain should spawn floating text.
    public const float NO_WORLD_LOCATION = float.MaxValue;

    public readonly ulong PlayerEntityId;
    public readonly ResourceType Type;
    public float Amount;
    public float Multiplier;

    // World/tile-space location this gain happened at (e.g. a dying tree's position for
    // OnDeathResourceDropSystem), or NO_WORLD_LOCATION if there isn't one.
    public float X = NO_WORLD_LOCATION;
    public float Y = NO_WORLD_LOCATION;

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

        ResourcesChangedEvent changed = new ResourcesChangedEvent
        {
            EntityId = PlayerEntityId,
            ClientId = ResourceHelper.GetOwnerPlayerId(ecs, PlayerEntityId),
            X = X,
            Y = Y,
        };

        switch (Type)
        {
            case ResourceType.Wood:       resources.Wood       += total; changed.WoodDelta       = total; break;
            case ResourceType.Stone:      resources.Stone      += total; changed.StoneDelta      = total; break;
            case ResourceType.Metal:      resources.Metal      += total; changed.MetalDelta      = total; break;
            case ResourceType.Gems:       resources.Gems       += total; changed.GemsDelta       = total; break;
            case ResourceType.Soulstones: resources.Soulstones += total; changed.SoulstonesDelta = total; break;
            case ResourceType.Gold:       resources.Gold       += total; changed.GoldDelta       = total; break;
        }

        ecs.Delta.MarkComponentDirty(PlayerEntityId, typeof(PlayerResourcesComponent));
        ecs.FlagEvents.Add(changed);
    }
}
