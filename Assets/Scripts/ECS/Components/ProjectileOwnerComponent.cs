public struct ProjectileOwnerComponent : IComponent
{
    // How many pooled projectile entities this troop owns (see ProjectileBaseComponent).
    public int MaxProjectiles;

    // Entity ID of the pooled projectile to try first the next time this troop fires.
    // Follow ProjectileBaseComponent.NextProjectileId from here to walk the pool ring
    // forward until an available (non-active) one is found.
    public ulong NextProjectileId;
}
