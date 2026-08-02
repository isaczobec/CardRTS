using System.Collections.Generic;

/// <summary>
/// Server-only (mirrors SpawnAtPointCardPlaySystem's own isServer gate, including the card
/// recycle step — a discard isn't predicted, so the hand-card visual only disappears once
/// CardDiscardedEvent arrives over ServerFlagEvents, exactly like a normal play). Reads
/// DiscardCardInput each tick: validates the card entity exists, belongs to the requesting
/// client, is currently in hand, and is NOT currently affordable (explicit design ask —
/// discarding is only meant as a way to get rid of a card you can't play right now, not a
/// free way to cycle an affordable one), then recycles it to the bottom of its owner's deck
/// (same DeckHelper call SpawnAtPointCardPlaySystem uses for a normal play) and applies a
/// temporary resource-gain debuff to the owner — a discard still has a real cost, even
/// though it's restricted to unaffordable cards.
/// </summary>
public static class DiscardCardSystem
{
    // Fraction of a normal Wood/Stone/Metal/Gold gain still received while debuffed — see
    // ResourceGainDebuffSystem. 0.3 = -70%.
    private const float ResourceGainDebuffMultiplierRatio = 0.3f;
    private const float ResourceGainDebuffDurationSeconds = 20f;

    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<DiscardCardInput> inputs = ecs.GetInputsForTick<DiscardCardInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();

        foreach (DiscardCardInput input in inputs)
            Discard(ecs, input, cardStore);
    }

    private static void Discard(ECS ecs, DiscardCardInput input, ComponentStore<CardComponent> cardStore)
    {
        if (!ecs.HasEntity(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[DiscardCardSystem] Rejected: entity {input.CardEntityId} does not exist (client {input.ClientId}).", "cards");
            return;
        }
        if (!cardStore.HasComponent(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[DiscardCardSystem] Rejected: entity {input.CardEntityId} has no CardComponent (client {input.ClientId}).", "cards");
            return;
        }

        CardComponent card = cardStore.GetComponent(input.CardEntityId);
        if (card.OwnerPlayerId != input.ClientId)
        {
            DebugLogger.LogWarning($"[DiscardCardSystem] Rejected: card {input.CardEntityId} owner {card.OwnerPlayerId} != requesting client {input.ClientId}.", "cards");
            return;
        }
        if (card.Location != CardLocation.Hand)
        {
            DebugLogger.LogWarning($"[DiscardCardSystem] Rejected: card {input.CardEntityId} is in {card.Location}, not Hand (client {input.ClientId}).", "cards");
            return;
        }

        if (!CardRegistry.TryGet(card.Type, out Card definition))
        {
            DebugLogger.LogWarning($"[DiscardCardSystem] Rejected: card type {card.Type} not found in CardRegistry (client {input.ClientId}).", "cards");
            return;
        }

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, card.OwnerPlayerId);
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId))
        {
            DebugLogger.LogWarning($"[DiscardCardSystem] Rejected: no PlayerResourcesComponent found for player {card.OwnerPlayerId} (client {input.ClientId}).", "cards");
            return;
        }
        if (definition.Cost.CanAfford(resourceStore.GetComponent(resourceEntityId)))
        {
            DebugLogger.LogWarning($"[DiscardCardSystem] Rejected: card {input.CardEntityId} ({card.Type}) is currently affordable — only unaffordable cards can be discarded (client {input.ClientId}).", "cards");
            return;
        }

        ref CardComponent discardedCard = ref cardStore.GetComponent(input.CardEntityId);
        discardedCard.Location = CardLocation.Deck;
        ecs.Delta.MarkComponentDirty(input.CardEntityId, typeof(CardComponent));

        DeckHelper.EnqueueToDeck(ecs, card.OwnerPlayerId, input.CardEntityId);

        ecs.FlagEvents.Add(new CardDiscardedEvent { EntityId = input.CardEntityId });

        ApplyResourceGainDebuff(ecs, resourceEntityId);
    }

    // Refreshes (rather than stacks) an already-active debuff on the same player, mirroring
    // CorrosionSystem's own "refresh the existing one" shape — repeated rapid discards reset
    // the 25s window instead of compounding the multiplier down toward zero.
    //
    // No ModifierID/RenderableModifierComponent — ModifierIconManager renders icons in world
    // space above ModifierComponent.TargetEntityId's PositionComponent, which is meaningful
    // for a troop but not for the player's own resource entity (see
    // NetworkManager.SpawnPlayerEntity — it carries a PositionComponent, but nothing ever
    // moves it to a sensible on-screen spot), so this stays icon-less rather than rendering
    // somewhere confusing.
    private static void ApplyResourceGainDebuff(ECS ecs, ulong resourceEntityId)
    {
        int durationTicks = TickManager.SecondsToTicks(ResourceGainDebuffDurationSeconds);

        ulong existingModifierId = ModifierQuery.FindActiveModifierId<ResourceGainDebuffComponent>(ecs, resourceEntityId);
        if (existingModifierId != 0)
        {
            ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
            ref ModifierComponent existing = ref modifierStore.GetComponent(existingModifierId);
            existing.TicksRemaining = durationTicks;
            ecs.Delta.MarkComponentDirty(existingModifierId, typeof(ModifierComponent));
            return;
        }

        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = resourceEntityId,
            TicksRemaining = durationTicks,
        });
        ecs.AddComponent(modifier.Id, new ResourceGainDebuffComponent
        {
            MultiplierRatio = ResourceGainDebuffMultiplierRatio,
        });
    }
}
