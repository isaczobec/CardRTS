public struct ProjectileBaseComponent : IComponent
{
    // The troop entity (ProjectileOwnerComponent) this projectile is pooled for.
    public ulong OwnerEntityId;

    // False while sitting idle in its owner's pool; true while actually in flight.
    public bool IsActive;

    // Entity ID of the next projectile in this owner's pool ring. Chaining these (starting
    // from ProjectileOwnerComponent.NextProjectileId) lets the owner walk forward to find
    // an available projectile on demand, instead of indexing into an array.
    public ulong NextProjectileId;
}
