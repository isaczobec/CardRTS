// Raised by RefundCardSystem once a refund is confirmed on the server — mirrors
// CardPlayedEvent exactly (same shape, same "server-confirmed, not predicted" contract via
// CardHandRenderer subscribing through ServerFlagEvents rather than the local prediction
// ECS's own FlagEvents).
public class CardRefundedEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    public override byte[] Serialize() => System.BitConverter.GetBytes(EntityId);
    public override void Deserialize(byte[] data) => EntityId = System.BitConverter.ToUInt64(data, 0);
}
