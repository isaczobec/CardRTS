public struct HealthComponent : IComponent
{
    public int CurrentHealth;

    // Entity that most recently damaged this one, set by DamageRequest.Execute. Defaults
    // to 0 ("none" — no entity ID is ever 0, see ECS.NextEntityId) until the first hit;
    // after that it may be DamageRequest.NO_DEALER_ENTITYID if that hit didn't specify a
    // dealer. Read by OnDeathResourceDropSystem to credit a kill.
    public ulong LastDamageDealer;
}
