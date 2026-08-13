using System.Collections.Generic;

/// <summary>
/// Server-only. Reads SpawnAtEntityInput each tick: validates the card entity exists,
/// belongs to the requesting client, is currently in hand, and is actually a
/// TargetEntityCard (see Card.cs); validates the target entity exists, is currently
/// selectable, and matches the card's CanTargetFriendly/CanTargetEnemyOrNeutral; then (if
/// the card requires it) range-checks the TARGET's own position against a friendly
/// building/troop, exactly the way SpawnAtPointCardPlaySystem range-checks a raw played
/// point — before dispatching to the matching Card definition's OnPlayed. The card entity
/// isn't deleted — it's recycled back into its owner's deck (at the back, via DeckHelper)
/// so DeckSystem eventually draws it again.
///
/// One of potentially several per-card-kind play systems (each pairing one InputBase
/// subtype with one Card subtype) — see Card.cs and SpawnAtPointCardPlaySystem, which this
/// mirrors closely.
/// </summary>
public static class TargetEntityCardPlaySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<SpawnAtEntityInput> inputs = ecs.GetInputsForTick<SpawnAtEntityInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();
        ComponentStore<SelectableComponent> selectableStore = ecs.GetComponentStore<SelectableComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        foreach (SpawnAtEntityInput input in inputs)
            PlayCard(ecs, input, cardStore, selectableStore, posStore);
    }

    private static void PlayCard(ECS ecs, SpawnAtEntityInput input,
        ComponentStore<CardComponent> cardStore, ComponentStore<SelectableComponent> selectableStore, ComponentStore<PositionComponent> posStore)
    {
        if (!ecs.HasEntity(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: entity {input.CardEntityId} does not exist (client {input.ClientId}).", "cards");
            return;
        }
        if (!cardStore.HasComponent(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: entity {input.CardEntityId} has no CardComponent (client {input.ClientId}).", "cards");
            return;
        }

        CardComponent card = cardStore.GetComponent(input.CardEntityId);
        if (card.OwnerPlayerId != input.ClientId)
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: card {input.CardEntityId} owner {card.OwnerPlayerId} != requesting client {input.ClientId}.", "cards");
            return;
        }
        if (card.Location != CardLocation.Hand)
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: card {input.CardEntityId} is in {card.Location}, not Hand (client {input.ClientId}).", "cards");
            return;
        }

        if (!CardRegistry.TryGet(card.Type, out Card definition))
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: card type {card.Type} not found in CardRegistry (client {input.ClientId}).", "cards");
            return;
        }

        // A SpawnAtEntityInput for a card that isn't actually a TargetEntityCard is never
        // legal — that card kind has its own InputBase subtype and play system instead.
        if (definition is not TargetEntityCard targetEntityCard)
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: card type {card.Type} is not a TargetEntityCard (client {input.ClientId}).", "cards");
            return;
        }

        if (!ecs.HasEntity(input.TargetEntityId) || selectableStore == null || !selectableStore.HasComponent(input.TargetEntityId))
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: target entity {input.TargetEntityId} does not exist or is not selectable.", "cards");
            return;
        }

        bool isFriendly = selectableStore.GetComponent(input.TargetEntityId).OwnerPlayerId == input.ClientId;
        if (isFriendly && !targetEntityCard.CanTargetFriendly || !isFriendly && !targetEntityCard.CanTargetEnemyOrNeutral)
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: card {card.Type} cannot target {(isFriendly ? "friendly" : "enemy/neutral")} entity {input.TargetEntityId}.", "cards");
            return;
        }

        bool isSelectable = ecs.Requests.Process(new IsSelectableRequest(input.TargetEntityId, input.ClientId), ecs, executeIfNotCancelled: false).IsSelectable;
        if (!isSelectable)
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: target entity {input.TargetEntityId} is not currently selectable.", "cards");
            return;
        }

        if (definition.RequiresFriendlyBuildingRange())
        {
            if (posStore == null || !posStore.HasComponent(input.TargetEntityId))
            {
                DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: target entity {input.TargetEntityId} has no position to range-check.", "cards");
                return;
            }

            PositionComponent targetPos = posStore.GetComponent(input.TargetEntityId);
            bool inBuildingRange = BuildingRangeHelper.IsWithinRangeOfFriendlyBuilding(ecs, card.OwnerPlayerId, targetPos.X, targetPos.Y, definition.MaxDistanceFromFriendlyBuilding);
            bool inTroopRange = definition.AllowsFriendlyTroopRange() &&
                TroopRangeHelper.IsWithinRangeOfFriendlyTroop(ecs, card.OwnerPlayerId, targetPos.X, targetPos.Y, definition.MaxDistanceFromFriendlyTroop);

            if (!inBuildingRange && !inTroopRange)
            {
                DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: target entity {input.TargetEntityId} is not within range of any friendly building or troop for player {card.OwnerPlayerId} (building range {definition.MaxDistanceFromFriendlyBuilding}, troop range {definition.MaxDistanceFromFriendlyTroop}).", "cards");
                return;
            }
        }

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, card.OwnerPlayerId);
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId))
        {
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: no PlayerResourcesComponent found for player {card.OwnerPlayerId} (resourceEntityId={resourceEntityId}, client {input.ClientId}).", "cards");
            return;
        }
        if (!definition.Cost.CanAfford(resourceStore.GetComponent(resourceEntityId)))
        {
            PlayerResourcesComponent res = resourceStore.GetComponent(resourceEntityId);
            DebugLogger.LogWarning($"[TargetEntityCardPlaySystem] Rejected: player {card.OwnerPlayerId} can't afford {card.Type} (cost W{definition.Cost.Wood}/S{definition.Cost.Stone}/M{definition.Cost.Metal}/G{definition.Cost.Gems}/So{definition.Cost.Soulstones}/Au{definition.Cost.Gold} vs have W{res.WoodFloor}/S{res.StoneFloor}/M{res.MetalFloor}/G{res.GemsFloor}/So{res.SoulstonesFloor}/Au{res.GoldFloor}).", "cards");
            return;
        }

        ecs.Requests.Process(new ResourcesDeductedRequest(resourceEntityId, definition.Cost), ecs);

        targetEntityCard.OnPlayed(ecs, input.CardEntityId, input.ClientId, input.TargetEntityId);
        ecs.FlagEvents.Add(new CardPlayedEvent { EntityId = input.CardEntityId });

        CardReturnHelper.OnCardPlayed(ecs, cardStore, input.CardEntityId, card.OwnerPlayerId, definition.Category);
    }
}
