// Granted by VengefulSpiritsUpgrade — attached to a permanent modifier entity
// (ModifierComponent.TargetEntityId = the troop/building it was equipped on, TicksRemaining
// = int.MaxValue), mirroring GiantsbaneComponent/CleaveComponent's own "payload lives on the
// modifier entity" shape. See VengefulSpiritsSystem, which finds this via
// ModifierQuery.ForEachActiveModifierId<VengefulSpiritsSourceComponent> every time its
// target deals direct damage, and fires a seeking projectile from ProjectilePoolOwnerId.
public struct VengefulSpiritsSourceComponent : IComponent
{
    // The entity actually carrying the ProjectileOwnerComponent this upgrade's pool lives
    // on — usually the wielder itself, but see ProjectilePool.AttachNewPoolOwner: a troop
    // that already has its own auto-attack pool (e.g. a ranged troop) gets this upgrade's
    // pool chained onto a separate linked entity instead, since a ProjectileOwnerComponent
    // only tracks a single pool ring by itself.
    public ulong ProjectilePoolOwnerId;
}
