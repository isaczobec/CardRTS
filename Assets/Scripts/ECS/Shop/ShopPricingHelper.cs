// Shared "what does this card actually cost to buy right now" rule — used by both
// BuyCardSystem (the authoritative charge) and ShopUIManager (the displayed price/
// affordability check), so the two can never disagree about what a purchase will cost.
//
// A player's first DiscountedPurchaseCount card purchases (across every CardType, not per
// type) always cost DiscountedShopGoldCost gold flat; every purchase after that reverts to
// the card's own Card.ShopGoldCost.
public static class ShopPricingHelper
{
    public const int DiscountedPurchaseCount = 6;
    public const int DiscountedShopGoldCost = 10;

    // Hard cap on how many cards a player may EVER buy from the shop in a single match,
    // regardless of gold — explicit design ask. Checked by both BuyCardSystem (the
    // authoritative reject) and ShopUIManager (the unaffordable-overlay display), so the two
    // can never disagree about when a player's hit the ceiling.
    public const int MaxCardsPurchased = 9;

    public static int GetEffectiveShopGoldCost(ECS ecs, ushort playerId, Card card)
    {
        if (GetCardsPurchased(ecs, playerId) < DiscountedPurchaseCount)
            return DiscountedShopGoldCost;

        return card.ShopGoldCost;
    }

    public static bool HasReachedPurchaseLimit(ECS ecs, ushort playerId)
        => GetCardsPurchased(ecs, playerId) >= MaxCardsPurchased;

    // ShopPurchaseHistoryComponent lives on the same entity as PlayerResourcesComponent (see
    // NetworkManager.SpawnPlayerEntity), so this reuses ResourceHelper's own lookup rather
    // than duplicating a second per-player entity search.
    public static int GetCardsPurchased(ECS ecs, ushort playerId)
    {
        ulong playerEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, playerId);
        if (playerEntityId == 0) return 0;

        ComponentStore<ShopPurchaseHistoryComponent> historyStore = ecs.GetComponentStore<ShopPurchaseHistoryComponent>();
        if (historyStore == null || !historyStore.HasComponent(playerEntityId)) return 0;

        return historyStore.GetComponent(playerEntityId).CardsPurchased;
    }
}
