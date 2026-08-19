// Added to a player's own entity (alongside PlayerComponent/PlayerResourcesComponent) —
// tracks how many cards that player has ever bought from the shop, split by CardCategory so
// BuyCardSystem/ShopUIManager can both apply the same per-category deck-composition cap (see
// ShopPricingHelper.MaxTroopsPurchased/MaxBuildingsPurchased/MaxSpellsPurchased) without
// disagreeing on whether a given purchase is still allowed. The combined total across all
// three also drives the shared "first N purchases are discounted" rule (see
// ShopPricingHelper.DiscountedPurchaseCount).
public struct ShopPurchaseHistoryComponent : IComponent
{
    public int TroopsPurchased;
    public int BuildingsPurchased;
    public int SpellsPurchased;
}
