// Raised whenever a projectile is fired that wants to declare a persistent visual link back
// to another entity — e.g. PirateCard's Hook ability, whose renderer (HookProjectileRenderer)
// draws a line between the hook projectile and the Pirate that fired it for as long as the
// hook exists. Distinct from the generic ProjectileActivatedEvent (which already carries
// OwnerEntityId/TargetEntityId and fires for every pooled projectile in the game) so a
// renderer that specifically cares about this kind of link doesn't have to filter through
// every other projectile firing in the game to find "mine" — see
// FireProjectileOnExpireSystem, which raises this immediately after
// ProjectilePool.FireInDirection succeeds.
public class LinkedProjectileFiredEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public ulong LinkedEntityId { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[16];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(LinkedEntityId).CopyTo(data, 8);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        LinkedEntityId = System.BitConverter.ToUInt64(data, 8);
    }
}
