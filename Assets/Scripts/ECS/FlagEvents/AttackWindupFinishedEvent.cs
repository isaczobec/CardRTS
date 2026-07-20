// Raised by BasicMeleeAISystem/BasicRangedAISystem.ResolveAttack once an attack windup's
// ticks reach 0 and it resolves — fires unconditionally, whether the hit/shot actually
// landed (target still valid, in range, CanPerform) or whiffed, but NOT when the windup is
// interrupted early via CancelAttack (a fresh move order cancelling it outright isn't
// "finishing"). See TroopBeginAttackEvent for the windup's start instead.
public class AttackWindupFinishedEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    public override byte[] Serialize() => System.BitConverter.GetBytes(EntityId);
    public override void Deserialize(byte[] data) => EntityId = System.BitConverter.ToUInt64(data, 0);
}
