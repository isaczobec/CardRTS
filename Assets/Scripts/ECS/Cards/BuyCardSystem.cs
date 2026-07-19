using System.Collections.Generic;

// Reads BuyCardInput each tick: validates the requested CardType is real and the buying
// player can afford its Card.ShopGoldCost, then deducts that cost via a
// ResourcesDeductedRequest. Unlike SpawnAtPointCardPlaySystem, this is NOT gated behind an
// isServer check at the top — resource deduction is just a component mutation and safe to
// predict (mirroring AbilitySystem's own reasoning), so a buying client sees its gold spent
// immediately instead of waiting on the server round-trip.
//
// Only the server actually creates the new card entity and enqueues it into the buyer's
// deck, though — an entity created during client prediction would get a different id than
// the server's own copy once confirmed (see ModifierComponent's doc comment for the same
// concern), and unlike a modifier's effect, a card sitting in a deck/hand has to be
// addressable by a stable id (CardHandRenderer keys everything off CardEntityId), so this
// can't be predicted the way e.g. AbilityManager's RingOfProjectilesAbility predicts its own
// modifier entity. The bought card simply pops into the client's deck once the server's
// delta confirms it, same as any other server-created entity.
public static class BuyCardSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<BuyCardInput> inputs = ecs.GetInputsForTick<BuyCardInput>();
        if (inputs == null || inputs.Count == 0) return;

        foreach (BuyCardInput input in inputs)
            BuyCard(ecs, input);
    }

    private static void BuyCard(ECS ecs, BuyCardInput input)
    {
        if (!CardRegistry.TryGet(input.CardType, out Card definition))
        {
            DebugLogger.LogWarning($"[BuyCardSystem] Rejected: card type {input.CardType} not found in CardRegistry (client {input.ClientId}).", "cards");
            return;
        }

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, input.ClientId);
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId))
        {
            DebugLogger.LogWarning($"[BuyCardSystem] Rejected: no PlayerResourcesComponent found for player {input.ClientId}.", "cards");
            return;
        }

        int shopGoldCost = ShopPricingHelper.GetEffectiveShopGoldCost(ecs, input.ClientId, definition);
        ResourceCost shopCost = new ResourceCost { Gold = shopGoldCost };
        PlayerResourcesComponent resources = resourceStore.GetComponent(resourceEntityId);
        if (!shopCost.CanAfford(resources))
        {
            DebugLogger.LogWarning($"[BuyCardSystem] Rejected: player {input.ClientId} can't afford {input.CardType} (shop cost {shopGoldCost} gold, has {resources.GoldFloor}).", "cards");
            return;
        }

        ecs.Requests.Process(new ResourcesDeductedRequest(resourceEntityId, shopCost), ecs);

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        EntityHandle entity = ecs.CreateEntity();
        ecs.AddComponent(entity.Id, new CardComponent
        {
            Type          = input.CardType,
            Location      = CardLocation.Deck,
            OwnerPlayerId = input.ClientId,
        });

        DeckHelper.EnqueueToDeck(ecs, input.ClientId, entity.Id);

        ComponentStore<ShopPurchaseHistoryComponent> historyStore = ecs.GetComponentStore<ShopPurchaseHistoryComponent>();
        if (historyStore != null && historyStore.HasComponent(resourceEntityId))
        {
            ref ShopPurchaseHistoryComponent history = ref historyStore.GetComponent(resourceEntityId);
            history.CardsPurchased++;
            ecs.Delta.MarkComponentDirty(resourceEntityId, typeof(ShopPurchaseHistoryComponent));
        }
    }
}
