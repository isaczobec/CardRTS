// Raised whenever a player's PlayerResourcesComponent changes, whether spent (see
// ResourceHelper.Spend) or gained (see ResourcesAdded.Execute, including passive
// per-tick generation). CardHandRenderer subscribes to this both to auto-deselect a
// selected card that's become unaffordable and to refresh each hand card's affordability
// coloring/overlay when resources rise back above a card's cost.
//
// Also carries the net per-resource delta, the id of the player whose resources these are
// (ResourceHelper.GetOwnerPlayerId) — FloatingTextManager compares this against
// NetworkManager.LocalPlayerId so a client only ever sees its own resource-gain callouts,
// not every player's — and, for gains tied to a spot in the world (e.g.
// OnDeathResourceDropSystem), the X/Y that happened at, skipping deltas with X/Y ==
// ResourcesAdded.NO_WORLD_LOCATION (spends and untargeted passive generation never set one).
public class ResourcesChangedEvent : FlagEvent
{
    public ulong EntityId { get; set; }
    public ushort ClientId { get; set; }
    public float WoodDelta { get; set; }
    public float StoneDelta { get; set; }
    public float MetalDelta { get; set; }
    public float GemsDelta { get; set; }
    public float SoulstonesDelta { get; set; }
    public float GoldDelta { get; set; }
    public float X { get; set; } = ResourcesAdded.NO_WORLD_LOCATION;
    public float Y { get; set; } = ResourcesAdded.NO_WORLD_LOCATION;

    public override byte[] Serialize()
    {
        byte[] data = new byte[42];
        System.BitConverter.GetBytes(EntityId).CopyTo(data, 0);
        System.BitConverter.GetBytes(ClientId).CopyTo(data, 8);
        System.BitConverter.GetBytes(WoodDelta).CopyTo(data, 10);
        System.BitConverter.GetBytes(StoneDelta).CopyTo(data, 14);
        System.BitConverter.GetBytes(MetalDelta).CopyTo(data, 18);
        System.BitConverter.GetBytes(GemsDelta).CopyTo(data, 22);
        System.BitConverter.GetBytes(SoulstonesDelta).CopyTo(data, 26);
        System.BitConverter.GetBytes(GoldDelta).CopyTo(data, 30);
        System.BitConverter.GetBytes(X).CopyTo(data, 34);
        System.BitConverter.GetBytes(Y).CopyTo(data, 38);
        return data;
    }

    public override void Deserialize(byte[] data)
    {
        EntityId = System.BitConverter.ToUInt64(data, 0);
        ClientId = System.BitConverter.ToUInt16(data, 8);
        WoodDelta = System.BitConverter.ToSingle(data, 10);
        StoneDelta = System.BitConverter.ToSingle(data, 14);
        MetalDelta = System.BitConverter.ToSingle(data, 18);
        GemsDelta = System.BitConverter.ToSingle(data, 22);
        SoulstonesDelta = System.BitConverter.ToSingle(data, 26);
        GoldDelta = System.BitConverter.ToSingle(data, 30);
        X = System.BitConverter.ToSingle(data, 34);
        Y = System.BitConverter.ToSingle(data, 38);
    }
}
