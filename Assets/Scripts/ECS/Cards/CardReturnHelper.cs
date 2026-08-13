// Shared "what happens to a card entity right after it's played" logic for every card-kind
// play system (SpawnAtPointCardPlaySystem/MultiPointCardPlaySystem/TargetEntityCardPlaySystem)
// — a Troop/Building category card is held at CardLocation.InPlay instead of being recycled
// straight back to the deck, since its own troop(s)/building(s) now count as being in play
// until they die (see SpawnedByCardComponent/CardReturnSystem, which performs the deferred
// recycle once every one of them is gone — including immediately, next tick, for a
// Troop/Building card whose play somehow tagged nothing at all). Every other category
// (Spell) keeps the original "recycle immediately" behavior.
public static class CardReturnHelper
{
    public static void OnCardPlayed(ECS ecs, ComponentStore<CardComponent> cardStore, ulong cardEntityId, ushort ownerPlayerId, CardCategory category)
    {
        ref CardComponent card = ref cardStore.GetComponent(cardEntityId);

        if (category == CardCategory.Troop || category == CardCategory.Building)
        {
            card.Location = CardLocation.InPlay;
            ecs.Delta.MarkComponentDirty(cardEntityId, typeof(CardComponent));
            return;
        }

        card.Location = CardLocation.Deck;
        ecs.Delta.MarkComponentDirty(cardEntityId, typeof(CardComponent));
        DeckHelper.EnqueueToDeck(ecs, ownerPlayerId, cardEntityId);
    }
}
