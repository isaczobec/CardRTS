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

    // The caster's own position, captured at the moment this was raised — see
    // PositionQuery.TryGet.
    public float X { get; set; }
    public float Y { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[16];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(X).CopyTo(data, 8);
        System.BitConverter.GetBytes(Y).CopyTo(data, 12);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        X = System.BitConverter.ToSingle(data, 8);
        Y = System.BitConverter.ToSingle(data, 12);
    }

    public override (float X, float Y)? Position => (X, Y);
}
