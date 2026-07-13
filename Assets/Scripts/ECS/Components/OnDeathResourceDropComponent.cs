// Attach to an entity to grant resources to whichever player owns the entity that last
// damaged it (HealthComponent.LastDamageDealer) when it dies — see OnDeathResourceDropSystem.
public struct OnDeathResourceDropComponent : IComponent
{
    public ResourceCost Drop;
}
