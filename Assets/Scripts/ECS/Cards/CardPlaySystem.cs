using System.Collections.Generic;

/// <summary>
/// Server-only. Reads CardPlayedInput each tick: validates the card entity exists,
/// belongs to the requesting client, and is currently in hand, then dispatches to the
/// matching Card definition's OnPlayed. The card entity isn't deleted — it's recycled
/// back into its owner's deck (at the back, via DeckHelper) so DeckSystem eventually
/// draws it again.
/// </summary>
public static class CardPlaySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<CardPlayedInput> inputs = ecs.GetInputsForTick<CardPlayedInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();

        foreach (CardPlayedInput input in inputs)
            PlayCard(ecs, input, cardStore);
    }

    private static void PlayCard(ECS ecs, CardPlayedInput input, ComponentStore<CardComponent> cardStore)
    {
        if (!ecs.HasEntity(input.CardEntityId)) return;
        if (!cardStore.HasComponent(input.CardEntityId)) return;

        CardComponent card = cardStore.GetComponent(input.CardEntityId);
        if (card.OwnerPlayerId != input.ClientId) return;
        if (card.Location != CardLocation.Hand) return;

        if (!CardRegistry.TryGet(card.Type, out Card definition)) return;

        definition.OnPlayed(ecs, input.CardEntityId, input.ClientId, input.X, input.Y);

        // Recycle the card back into its owner's deck (at the back) rather than
        // deleting it.
        ref CardComponent playedCard = ref cardStore.GetComponent(input.CardEntityId);
        playedCard.Location = CardLocation.Deck;
        ecs.Delta.MarkComponentDirty(input.CardEntityId, typeof(CardComponent));

        DeckHelper.EnqueueToDeck(ecs, card.OwnerPlayerId, input.CardEntityId);
    }
}
