// Shared per-player deck-composition/pricing rules — used by both BuyCardSystem (the
// authoritative charge/reject) and ShopUIManager (the displayed price/affordability check),
// so the two can never disagree about what a purchase will cost or whether it's still allowed.
//
// A player's first DiscountedPurchaseCount card purchases (across every CardType/category
// combined) always cost DiscountedShopGoldCost gold flat; every purchase after that reverts to
// the card's own Card.ShopGoldCost.
public static class ShopPricingHelper
{
    public const int DiscountedPurchaseCount = 8;
    public const int DiscountedShopGoldCost = 10;

    // Per-category deck-composition caps — explicit design ask, replacing the old flat
    // MaxCardsPurchased-across-everything limit. Checked by both BuyCardSystem (the
    // authoritative reject) and ShopUIManager (the unaffordable-overlay display), so the two
    // can never disagree about when a player's hit the ceiling for a given category.
    public const int MaxTroopsPurchased = 4;
    public const int MaxBuildingsPurchased = 2;
    public const int MaxSpellsPurchased = 2;

    public static int GetEffectiveShopGoldCost(ECS ecs, ushort playerId, Card card)
    {
        if (GetTotalCardsPurchased(ecs, playerId) < DiscountedPurchaseCount)
            return DiscountedShopGoldCost;

        return card.ShopGoldCost;
    }

    public static bool HasReachedPurchaseLimit(ECS ecs, ushort playerId, CardCategory category)
    {
        ShopPurchaseHistoryComponent history = GetHistory(ecs, playerId);
        return category switch
        {
            CardCategory.Building => history.BuildingsPurchased >= MaxBuildingsPurchased,
            CardCategory.Spell    => history.SpellsPurchased >= MaxSpellsPurchased,
            _                     => history.TroopsPurchased >= MaxTroopsPurchased,
        };
    }

    public static int GetTotalCardsPurchased(ECS ecs, ushort playerId)
    {
        ShopPurchaseHistoryComponent history = GetHistory(ecs, playerId);
        return history.TroopsPurchased + history.BuildingsPurchased + history.SpellsPurchased;
    }

    // ShopPurchaseHistoryComponent lives on the same entity as PlayerResourcesComponent (see
    // NetworkManager.SpawnPlayerEntity), so this reuses ResourceHelper's own lookup rather
    // than duplicating a second per-player entity search.
    private static ShopPurchaseHistoryComponent GetHistory(ECS ecs, ushort playerId)
    {
        ulong playerEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, playerId);
        if (playerEntityId == 0) return default;

        ComponentStore<ShopPurchaseHistoryComponent> historyStore = ecs.GetComponentStore<ShopPurchaseHistoryComponent>();
        if (historyStore == null || !historyStore.HasComponent(playerEntityId)) return default;

        return historyStore.GetComponent(playerEntityId);
    }
}
