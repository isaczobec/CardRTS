// Static per-CardType price, read from Card.Cost and compared against a player's
// PlayerResourcesComponent (via its *Floor properties) to gate card selection/play.
// Fields mirror ResourceType/PlayerResourcesComponent 1:1; 0 means the card doesn't cost
// that resource at all.
public struct ResourceCost
{
    public int Wood;
    public int Stone;
    public int Metal;
    public int Gems;
    public int Soulstones;
    public int Gold;

    public bool CanAfford(PlayerResourcesComponent resources) =>
        resources.WoodFloor       >= Wood &&
        resources.StoneFloor      >= Stone &&
        resources.MetalFloor      >= Metal &&
        resources.GemsFloor       >= Gems &&
        resources.SoulstonesFloor >= Soulstones &&
        resources.GoldFloor       >= Gold;
}
