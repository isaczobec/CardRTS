public class TroopBeginAttackEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    // 0 = no target (shouldn't normally happen — BasicMeleeAISystem always has an
    // active target when it fires this — but renderers should treat 0 as "none").
    public ulong TargetEntityId { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[16];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(TargetEntityId).CopyTo(data, 8);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        TargetEntityId = System.BitConverter.ToUInt64(data, 8);
    }
}
