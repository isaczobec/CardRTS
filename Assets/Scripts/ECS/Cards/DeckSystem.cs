using System;

// Each tick, for every player (PlayerDeckComponent entity): if their hand has fewer than
// MaxHandSize cards, starts a DrawCooldownSeconds countdown (unless one is already
// running); once it reaches zero, draws the card at the top of the deck into hand. If the
// player is still short a card afterwards, the next tick immediately starts a fresh
// cooldown — so a player below the hand cap keeps drawing every DrawCooldownSeconds until
// the hand is full or the deck runs dry.
// Component mutation only (no entity creation/deletion), so — like BasicMeleeAISystem —
// this runs unconditionally on every ECS it's registered on, predicted on clients the
// same as everything else.
public class DeckSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private const int MaxHandSize = 6;
    private const float DrawCooldownSeconds = 3f;

    public void Execute(ECS ecs)
    {
        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<PlayerDeckComponent> deckStore = ecs.GetComponentStore<PlayerDeckComponent>();
        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();

        deckStore.ForEach((ulong deckEntityId) => Tick(ecs, deckEntityId, playerStore, deckStore, cardStore));
    }

    private void Tick(ECS ecs, ulong deckEntityId,
        ComponentStore<PlayerComponent> playerStore,
        ComponentStore<PlayerDeckComponent> deckStore,
        ComponentStore<CardComponent> cardStore)
    {
        if (!playerStore.HasComponent(deckEntityId)) return;

        ushort ownerPlayerId = playerStore.GetComponent(deckEntityId).PlayerId;
        ref PlayerDeckComponent deck = ref deckStore.GetComponent(deckEntityId);

        if (CountHand(cardStore, ownerPlayerId) >= MaxHandSize)
        {
            if (deck.DrawCooldownTicksRemaining != 0)
            {
                deck.DrawCooldownTicksRemaining = 0;
                ecs.Delta.MarkComponentDirty(deckEntityId, typeof(PlayerDeckComponent));
            }
            return;
        }

        if (deck.DeckHeadId == 0) return; // nothing left to draw

        if (deck.DrawCooldownTicksRemaining <= 0)
        {
            deck.DrawCooldownTicksRemaining = TickManager.SecondsToTicks(DrawCooldownSeconds);
            ecs.Delta.MarkComponentDirty(deckEntityId, typeof(PlayerDeckComponent));
            return;
        }

        deck.DrawCooldownTicksRemaining--;
        if (deck.DrawCooldownTicksRemaining > 0)
        {
            ecs.Delta.MarkComponentDirty(deckEntityId, typeof(PlayerDeckComponent));
            return;
        }

        // Cooldown just finished — draw the top card into hand.
        ulong drawnId = DeckHelper.DequeueFromDeck(ecs, deckEntityId);
        if (drawnId != 0 && cardStore.HasComponent(drawnId))
        {
            ref CardComponent drawn = ref cardStore.GetComponent(drawnId);
            drawn.Location = CardLocation.Hand;
            ecs.Delta.MarkComponentDirty(drawnId, typeof(CardComponent));
        }
    }

    private static int CountHand(ComponentStore<CardComponent> cardStore, ushort ownerPlayerId)
    {
        int count = 0;
        cardStore.ForEach((ulong id) =>
        {
            CardComponent card = cardStore.GetComponent(id);
            if (card.OwnerPlayerId == ownerPlayerId && card.Location == CardLocation.Hand)
                count++;
        });
        return count;
    }
}
