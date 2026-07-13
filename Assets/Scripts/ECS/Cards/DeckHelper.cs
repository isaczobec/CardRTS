// Shared per-player deck queue operations. A player's deck is a FIFO queue of card
// entities (see PlayerDeckComponent for the head/tail pointers and
// CardComponent.NextInDeckId for the links between them). A card-kind's play system (e.g.
// SpawnAtPointCardPlaySystem) enqueues onto the tail when a card is played; DeckSystem
// dequeues the head into hand once its draw cooldown finishes.
public static class DeckHelper
{
    const int StartingCopiesPerCardType = 3;

    // Creates copiesPerType card entities of every CardType, all starting in the deck, and
    // enqueues them onto ownerPlayerId's deck. Called once per player at game start, after
    // that player's PlayerDeckComponent already exists (see NetworkManager.SpawnPlayerEntity).
    public static void SeedStartingDeck(ECS ecs, ushort ownerPlayerId, int copiesPerType = StartingCopiesPerCardType)
    {
        foreach (CardType type in System.Enum.GetValues(typeof(CardType)))
        {
            for (int i = 0; i < copiesPerType; i++)
            {
                EntityHandle entity = ecs.CreateEntity();
                ecs.AddComponent(entity.Id, new CardComponent
                {
                    Type          = type,
                    Location      = CardLocation.Deck,
                    OwnerPlayerId = ownerPlayerId,
                });

                EnqueueToDeck(ecs, ownerPlayerId, entity.Id);
            }
        }
    }

    // Finds the entity carrying ownerPlayerId's PlayerDeckComponent, or 0 if none exists
    // (e.g. that player's entity hasn't been set up yet).
    public static ulong FindPlayerDeckEntity(ECS ecs, ushort ownerPlayerId)
    {
        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<PlayerDeckComponent> deckStore = ecs.GetComponentStore<PlayerDeckComponent>();
        if (playerStore == null || deckStore == null) return 0;

        ulong found = 0;
        playerStore.ForEach((ulong id) =>
        {
            if (found != 0) return;
            if (!deckStore.HasComponent(id)) return;
            if (playerStore.GetComponent(id).PlayerId == ownerPlayerId) found = id;
        });
        return found;
    }

    // Appends cardEntityId to the back of ownerPlayerId's deck queue. No-ops if that
    // player has no deck or the card entity/component is gone.
    public static void EnqueueToDeck(ECS ecs, ushort ownerPlayerId, ulong cardEntityId)
    {
        ulong deckEntityId = FindPlayerDeckEntity(ecs, ownerPlayerId);
        if (deckEntityId == 0) return;

        ComponentStore<PlayerDeckComponent> deckStore = ecs.GetComponentStore<PlayerDeckComponent>();
        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();
        if (!cardStore.HasComponent(cardEntityId)) return;

        ref CardComponent card = ref cardStore.GetComponent(cardEntityId);
        card.NextInDeckId = 0;
        ecs.Delta.MarkComponentDirty(cardEntityId, typeof(CardComponent));

        ref PlayerDeckComponent deck = ref deckStore.GetComponent(deckEntityId);
        if (deck.DeckTailId != 0 && cardStore.HasComponent(deck.DeckTailId))
        {
            ref CardComponent tailCard = ref cardStore.GetComponent(deck.DeckTailId);
            tailCard.NextInDeckId = cardEntityId;
            ecs.Delta.MarkComponentDirty(deck.DeckTailId, typeof(CardComponent));
        }

        deck.DeckTailId = cardEntityId;
        if (deck.DeckHeadId == 0)
            deck.DeckHeadId = cardEntityId;

        ecs.Delta.MarkComponentDirty(deckEntityId, typeof(PlayerDeckComponent));
    }

    // Removes and returns the entity ID at the front of deckEntityId's queue (0 if
    // empty), advancing the head pointer to whatever was behind it.
    public static ulong DequeueFromDeck(ECS ecs, ulong deckEntityId)
    {
        ComponentStore<PlayerDeckComponent> deckStore = ecs.GetComponentStore<PlayerDeckComponent>();
        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();
        if (!deckStore.HasComponent(deckEntityId)) return 0;

        ref PlayerDeckComponent deck = ref deckStore.GetComponent(deckEntityId);
        ulong headId = deck.DeckHeadId;
        if (headId == 0 || !cardStore.HasComponent(headId)) return 0;

        ref CardComponent headCard = ref cardStore.GetComponent(headId);
        deck.DeckHeadId = headCard.NextInDeckId;
        if (deck.DeckHeadId == 0)
            deck.DeckTailId = 0;

        headCard.NextInDeckId = 0;
        ecs.Delta.MarkComponentDirty(headId, typeof(CardComponent));
        ecs.Delta.MarkComponentDirty(deckEntityId, typeof(PlayerDeckComponent));

        return headId;
    }
}
