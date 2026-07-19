// Added to a player's own entity (alongside PlayerComponent/PlayerResourcesComponent) —
// tracks how many cards that player has ever bought from the shop, so BuyCardSystem/
// ShopUIManager can both apply the same "first N purchases are discounted" rule (see
// ShopPricingHelper) without disagreeing on the price a given purchase will actually cost.
public struct ShopPurchaseHistoryComponent : IComponent
{
    public int CardsPurchased;
}
