using System.Collections.Generic;

// Each tick, recycles every CardLocation.InPlay card back into its owner's deck once none of
// the entities it spawned (see SpawnedByCardComponent) are alive anymore — the deferred
// counterpart to CardReturnHelper, which is what puts a Troop/Building card into InPlay in
// the first place instead of recycling it immediately. Also covers a Troop/Building card
// whose play somehow tagged nothing at all: it just gets recycled the very next tick, same
// as if its single spawn had already died.
//
// A recalled troop/building is deleted by RecallSystem the same way a normal death deletes
// one (ecs.DeleteEntity strips SpawnedByCardComponent off it either way), so recalling every
// remaining spawn returns its card exactly as fast as killing them all would.
//
// Component mutation only (no entity creation/deletion) — like DeckSystem, this runs
// unconditionally on every ECS it's registered on, predicted on clients the same as
// everything else.
public static class CardReturnSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    // Card entity ids with at least one currently-alive spawn — rebuilt from scratch each
    // tick in one pass over SpawnedByCardComponent, rather than re-scanning that store once
    // per InPlay card (see SpawnedByCardHelper.CollectAliveSpawns, which does the latter and
    // is fine for RecallSystem's much rarer per-input use, but would be wasteful here).
    private static readonly HashSet<ulong> _cardsWithAliveSpawns = new HashSet<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();
        ComponentStore<SpawnedByCardComponent> spawnedByStore = ecs.GetComponentStore<SpawnedByCardComponent>();
        if (cardStore == null || spawnedByStore == null) return;

        _cardsWithAliveSpawns.Clear();
        spawnedByStore.ForEach((ulong id) =>
            _cardsWithAliveSpawns.Add(spawnedByStore.GetComponent(id).CardEntityId));

        cardStore.ForEach((ulong cardEntityId) => TryReturnCard(ecs, cardStore, cardEntityId));
    }

    private static void TryReturnCard(ECS ecs, ComponentStore<CardComponent> cardStore, ulong cardEntityId)
    {
        ref CardComponent card = ref cardStore.GetComponent(cardEntityId);
        if (card.Location != CardLocation.InPlay) return;
        if (_cardsWithAliveSpawns.Contains(cardEntityId)) return;

        ushort ownerPlayerId = card.OwnerPlayerId;
        card.Location = CardLocation.Deck;
        ecs.Delta.MarkComponentDirty(cardEntityId, typeof(CardComponent));

        DeckHelper.EnqueueToDeck(ecs, ownerPlayerId, cardEntityId);
    }
}
