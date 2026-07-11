// Raised whenever a player's PlayerResourcesComponent changes, whether spent (see
// ResourceHelper.Spend) or gained (see ResourcesAdded.Execute, including passive
// per-tick generation). CardHandRenderer subscribes to this both to auto-deselect a
// selected card that's become unaffordable and to refresh each hand card's affordability
// coloring/overlay when resources rise back above a card's cost.
public class ResourcesChangedEvent : FlagEvent
{
    public ulong EntityId { get; set; }

    public override byte[] Serialize() => System.BitConverter.GetBytes(EntityId);
    public override void Deserialize(byte[] data) => EntityId = System.BitConverter.ToUInt64(data, 0);
}
