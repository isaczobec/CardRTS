public struct ProjectileOwnerComponent : IComponent
{
    // How many pooled projectile entities this troop owns (see ProjectileBaseComponent).
    public int MaxProjectiles;

    // Entity ID of the pooled projectile to try first the next time this troop fires.
    // Follow ProjectileBaseComponent.NextProjectileId from here to walk the pool ring
    // forward until an available (non-active) one is found.
    public ulong NextProjectileId;

    // Entity ID of another ProjectileOwnerComponent-carrying entity holding a second
    // (third, ...) pool for the same troop — e.g. a faster/longer-range projectile kind
    // used by an ability instead of the troop's own default auto-attack pool. 0 if there
    // isn't one. Chain these to support any number of extra pools per troop; see
    // ProjectilePool.ResolveOwnerAtIndex for walking this list by index, and Ability.
    // ProjectileOwnerIndex for how an ability picks which pool in the chain to fire from.
    // The troop's own PRIMARY pool (this component, wherever it's added) is always index 0;
    // this field is index 1, its own NextProjectileOwnerId (on that entity) is index 2, etc.
    public ulong NextProjectileOwnerId;
}
