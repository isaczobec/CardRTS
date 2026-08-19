using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-only (mirrors SpawnAtPointCardPlaySystem's own isServer gate) — a refund isn't
/// predicted, so the hand-card visual only disappears once CardRefundedEvent arrives over
/// ServerFlagEvents, exactly like a normal play. Reads RefundCardInput each tick: validates
/// the card entity exists, belongs to the requesting client, and is currently in hand, then
/// permanently sells it — unlike the discard mechanic this replaces, ANY hand card can be
/// refunded, not just an unaffordable one (a refund already has a real cost: half its value),
/// and the card is deleted outright rather than recycled back into the deck.
///
/// The player is refunded 50% of the card's own FULL Card.ShopGoldCost (never the discounted
/// price this specific copy may have actually been bought for — explicit design ask), plus
/// 50% of the combined ShopGoldCost of every UpgradeComponent entity currently equipped on it
/// (see UpgradeComponent's own doc comment for why an upgrade is its own entity rather than a
/// slot on the card) — those upgrade entities are deleted right along with the card.
/// </summary>
public static class RefundCardSystem
{
    private const float RefundRatio = 0.5f;

    // Scratch buffer for the equipped-upgrade scan below — reused across calls rather than
    // allocated per refund, mirrors AoeRootCard's own _queryBuffer.
    private static readonly List<ulong> _upgradeIdBuffer = new List<ulong>();

    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<RefundCardInput> inputs = ecs.GetInputsForTick<RefundCardInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();

        foreach (RefundCardInput input in inputs)
            Refund(ecs, input, cardStore);
    }

    private static void Refund(ECS ecs, RefundCardInput input, ComponentStore<CardComponent> cardStore)
    {
        if (!ecs.HasEntity(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[RefundCardSystem] Rejected: entity {input.CardEntityId} does not exist (client {input.ClientId}).", "cards");
            return;
        }
        if (!cardStore.HasComponent(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[RefundCardSystem] Rejected: entity {input.CardEntityId} has no CardComponent (client {input.ClientId}).", "cards");
            return;
        }

        CardComponent card = cardStore.GetComponent(input.CardEntityId);
        if (card.OwnerPlayerId != input.ClientId)
        {
            DebugLogger.LogWarning($"[RefundCardSystem] Rejected: card {input.CardEntityId} owner {card.OwnerPlayerId} != requesting client {input.ClientId}.", "cards");
            return;
        }
        if (card.Location != CardLocation.Hand)
        {
            DebugLogger.LogWarning($"[RefundCardSystem] Rejected: card {input.CardEntityId} is in {card.Location}, not Hand (client {input.ClientId}).", "cards");
            return;
        }

        if (!CardRegistry.TryGet(card.Type, out Card definition))
        {
            DebugLogger.LogWarning($"[RefundCardSystem] Rejected: card type {card.Type} not found in CardRegistry (client {input.ClientId}).", "cards");
            return;
        }

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, card.OwnerPlayerId);
        if (resourceEntityId == 0)
        {
            DebugLogger.LogWarning($"[RefundCardSystem] Rejected: no PlayerResourcesComponent found for player {card.OwnerPlayerId} (client {input.ClientId}).", "cards");
            return;
        }

        // Undoes BuyCardSystem's own increment — a refunded card frees back up the category
        // slot (and, since GetTotalCardsPurchased/DiscountedPurchaseCount just sum these same
        // three counters, potentially a discounted-price slot too) it used when bought.
        // Explicit design ask ("if I have bought 4 [troops] and then refund one, I should be
        // able to buy one more").
        ComponentStore<ShopPurchaseHistoryComponent> historyStore = ecs.GetComponentStore<ShopPurchaseHistoryComponent>();
        if (historyStore != null && historyStore.HasComponent(resourceEntityId))
        {
            ref ShopPurchaseHistoryComponent history = ref historyStore.GetComponent(resourceEntityId);
            switch (definition.Category)
            {
                case CardCategory.Building: history.BuildingsPurchased--; break;
                case CardCategory.Spell:    history.SpellsPurchased--; break;
                default:                    history.TroopsPurchased--; break;
            }
            ecs.Delta.MarkComponentDirty(resourceEntityId, typeof(ShopPurchaseHistoryComponent));
        }

        int upgradeGoldValue = CollectAndClearUpgrades(ecs, input.CardEntityId);

        int refundGold = Mathf.RoundToInt(definition.ShopGoldCost * RefundRatio)
            + Mathf.RoundToInt(upgradeGoldValue * RefundRatio);

        foreach (ulong upgradeId in _upgradeIdBuffer)
            ecs.DeleteEntity(upgradeId);

        ecs.DeleteEntity(input.CardEntityId);

        if (refundGold > 0)
            ecs.Requests.CreateRequest(new ResourcesAdded(resourceEntityId, ResourceType.Gold, refundGold));

        ecs.FlagEvents.Add(new CardRefundedEvent { EntityId = input.CardEntityId });
    }

    // Fills _upgradeIdBuffer with every UpgradeComponent entity targeting cardEntityId and
    // returns their combined ShopGoldCost — the caller deletes each id once it's also done
    // reading CardComponent/everything else off the card entity itself.
    private static int CollectAndClearUpgrades(ECS ecs, ulong cardEntityId)
    {
        _upgradeIdBuffer.Clear();

        ComponentStore<UpgradeComponent> upgradeStore = ecs.GetComponentStore<UpgradeComponent>();
        if (upgradeStore == null) return 0;

        int totalGoldValue = 0;
        upgradeStore.ForEach((ulong upgradeId) =>
        {
            UpgradeComponent upgrade = upgradeStore.GetComponent(upgradeId);
            if (upgrade.TargetCardEntityId != cardEntityId) return;

            if (UpgradeRegistry.TryGet(upgrade.Type, out CardUpgrade upgradeDefinition))
                totalGoldValue += upgradeDefinition.ShopGoldCost;

            _upgradeIdBuffer.Add(upgradeId);
        });

        return totalGoldValue;
    }
}
