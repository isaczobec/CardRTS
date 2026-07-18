using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-only. Reads MultiPointInput each tick: validates the card entity exists, belongs
/// to the requesting client, is currently in hand, is actually a MultiPointCard (see
/// Card.cs), and that the right number of points were sent, then dispatches to the matching
/// Card definition's OnPlayed. Range-checked against the first point only (where the card is
/// "cast from") — mirrors SpawnAtPointCardPlaySystem's validation chain otherwise. The card
/// entity isn't deleted — it's recycled back into its owner's deck (at the back, via
/// DeckHelper) so DeckSystem eventually draws it again.
/// </summary>
public static class MultiPointCardPlaySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<MultiPointInput> inputs = ecs.GetInputsForTick<MultiPointInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();

        foreach (MultiPointInput input in inputs)
            PlayCard(ecs, input, cardStore);
    }

    private static void PlayCard(ECS ecs, MultiPointInput input, ComponentStore<CardComponent> cardStore)
    {
        if (!ecs.HasEntity(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: entity {input.CardEntityId} does not exist (client {input.ClientId}).", "cards");
            return;
        }
        if (!cardStore.HasComponent(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: entity {input.CardEntityId} has no CardComponent (client {input.ClientId}).", "cards");
            return;
        }

        CardComponent card = cardStore.GetComponent(input.CardEntityId);
        if (card.OwnerPlayerId != input.ClientId)
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: card {input.CardEntityId} owner {card.OwnerPlayerId} != requesting client {input.ClientId}.", "cards");
            return;
        }
        if (card.Location != CardLocation.Hand)
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: card {input.CardEntityId} is in {card.Location}, not Hand (client {input.ClientId}).", "cards");
            return;
        }

        if (!CardRegistry.TryGet(card.Type, out Card definition))
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: card type {card.Type} not found in CardRegistry (client {input.ClientId}).", "cards");
            return;
        }

        // A MultiPointInput for a card that isn't actually a MultiPointCard is never legal —
        // that card kind has its own InputBase subtype and play system instead.
        if (definition is not MultiPointCard multiPointCard)
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: card type {card.Type} is not a MultiPointCard (client {input.ClientId}).", "cards");
            return;
        }

        if (input.Points == null || input.Points.Count != multiPointCard.PointCount)
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: card type {card.Type} expects {multiPointCard.PointCount} points, got {input.Points?.Count ?? 0} (client {input.ClientId}).", "cards");
            return;
        }

        // Re-checked here rather than trusted from the client — CardHandRenderer/
        // CardPlacementIndicatorManager already clamp every point after the first to
        // MaxRangeFromPreviousPoint on their end, but a modified/malicious client could send
        // unclamped points directly.
        float maxRangeFromPreviousPoint = multiPointCard.MaxRangeFromPreviousPoint;
        for (int i = 1; i < input.Points.Count; i++)
        {
            float distance = Vector2.Distance(input.Points[i - 1], input.Points[i]);
            if (distance > maxRangeFromPreviousPoint)
            {
                DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: point {i} is {distance} from point {i - 1}, exceeding MaxRangeFromPreviousPoint {maxRangeFromPreviousPoint} for card type {card.Type} (client {input.ClientId}).", "cards");
                return;
            }
        }

        if (definition.RequiresFriendlyBuildingRange())
        {
            Vector2 firstPoint = input.Points[0];
            bool inBuildingRange = BuildingRangeHelper.IsWithinRangeOfFriendlyBuilding(ecs, card.OwnerPlayerId, firstPoint.x, firstPoint.y, definition.MaxDistanceFromFriendlyBuilding);
            bool inTroopRange = definition.AllowsFriendlyTroopRange() &&
                TroopRangeHelper.IsWithinRangeOfFriendlyTroop(ecs, card.OwnerPlayerId, firstPoint.x, firstPoint.y, definition.MaxDistanceFromFriendlyTroop);

            if (!inBuildingRange && !inTroopRange)
            {
                DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: ({firstPoint.x}, {firstPoint.y}) is not within range of any friendly building or troop for player {card.OwnerPlayerId} (building range {definition.MaxDistanceFromFriendlyBuilding}, troop range {definition.MaxDistanceFromFriendlyTroop}).", "cards");
                return;
            }
        }

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, card.OwnerPlayerId);
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId))
        {
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: no PlayerResourcesComponent found for player {card.OwnerPlayerId} (resourceEntityId={resourceEntityId}, client {input.ClientId}).", "cards");
            return;
        }
        if (!definition.Cost.CanAfford(resourceStore.GetComponent(resourceEntityId)))
        {
            PlayerResourcesComponent res = resourceStore.GetComponent(resourceEntityId);
            DebugLogger.LogWarning($"[MultiPointCardPlaySystem] Rejected: player {card.OwnerPlayerId} can't afford {card.Type} (cost W{definition.Cost.Wood}/S{definition.Cost.Stone}/M{definition.Cost.Metal}/G{definition.Cost.Gems}/So{definition.Cost.Soulstones}/Au{definition.Cost.Gold} vs have W{res.WoodFloor}/S{res.StoneFloor}/M{res.MetalFloor}/G{res.GemsFloor}/So{res.SoulstonesFloor}/Au{res.GoldFloor}).", "cards");
            return;
        }

        ecs.Requests.Process(new ResourcesDeductedRequest(resourceEntityId, definition.Cost), ecs);

        multiPointCard.OnPlayed(ecs, input.CardEntityId, input.ClientId, input.Points);
        ecs.FlagEvents.Add(new CardPlayedEvent { EntityId = input.CardEntityId });

        // Recycle the card back into its owner's deck (at the back) rather than
        // deleting it.
        ref CardComponent playedCard = ref cardStore.GetComponent(input.CardEntityId);
        playedCard.Location = CardLocation.Deck;
        ecs.Delta.MarkComponentDirty(input.CardEntityId, typeof(CardComponent));

        DeckHelper.EnqueueToDeck(ecs, card.OwnerPlayerId, input.CardEntityId);
    }
}
