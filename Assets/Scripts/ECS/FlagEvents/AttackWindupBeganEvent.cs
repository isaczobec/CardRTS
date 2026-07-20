// Raised wherever an action windup actually begins (e.g. AbilityManager's skillshot
// ability, right where it creates its ActionWindupComponent modifier) — separate from
// TroopBeginAttackEvent, which is specifically BasicMeleeAISystem/BasicRangedAISystem's own
// auto-attack windup. Any future ability/effect that grants an ActionWindupComponent
// modifier should raise this too, from wherever it creates that modifier (a system's own
// Execute, never a FlagEvent subscriber — see the FlagEvent-subscriber-safety note on
// ModifierComponent).
public class AttackWindupBeganEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    public override byte[] Serialize() => System.BitConverter.GetBytes(EntityId);
    public override void Deserialize(byte[] data) => EntityId = System.BitConverter.ToUInt64(data, 0);
}
