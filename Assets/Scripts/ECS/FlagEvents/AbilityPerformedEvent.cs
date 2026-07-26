// Raised by AbilitySystem.CommitCooldown right after any of the three ExecuteInstant/
// ExecuteAtLocation/ExecuteOnEntity delegates runs for a successful cast — fires for every
// ability kind uniformly, from the one place all three converge, rather than duplicated at
// each of AbilitySystem's three call sites. Slot is 0-3 (Q/W/E/R — same convention as
// AbilityComponent.GetAbilityId), not the ability's own ID, so renderers can key off
// "whichever slot this troop happens to have this animation wired to" without needing to
// know about AbilityManager's IDs at all — see VariedAttackTroopRenderer's
// _abilityAnimationTriggers.
//
// WorldX/WorldY is the point the ability was actually aimed at: the clicked point itself
// for a TargetLocation ability, the target's position for a TargetEntity ability, or the
// caster's own position for an Instant ability (no click point exists there — facing
// "yourself" is a harmless no-op for anything that turns toward it).
public class AbilityPerformedEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public int Slot { get; set; }
    public float WorldX { get; set; }
    public float WorldY { get; set; }

    public override byte[] Serialize()
    {
        byte[] data = new byte[20];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(Slot).CopyTo(data, 8);
        System.BitConverter.GetBytes(WorldX).CopyTo(data, 12);
        System.BitConverter.GetBytes(WorldY).CopyTo(data, 16);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        Slot = System.BitConverter.ToInt32(data, 8);
        WorldX = System.BitConverter.ToSingle(data, 12);
        WorldY = System.BitConverter.ToSingle(data, 16);
    }

    public override (float X, float Y)? Position => (WorldX, WorldY);
}
