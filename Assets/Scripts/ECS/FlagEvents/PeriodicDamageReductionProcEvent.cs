// Raised by PeriodicDamageReductionSystem whenever a PeriodicDamageReductionComponent
// modifier's Nth-hit reduction actually triggers on EntityId — see
// PeriodicDamageReductionProcRenderer for the client-side VFX/SFX this drives.
public class PeriodicDamageReductionProcEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    public override byte[] Serialize() => System.BitConverter.GetBytes(EntityId);
    public override void Deserialize(byte[] data) => EntityId = System.BitConverter.ToUInt64(data, 0);
}
