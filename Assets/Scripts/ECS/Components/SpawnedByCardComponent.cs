// Tags an entity as one of the troop(s)/building(s) a specific played card instance spawned
// — attached (directly or, for an indirectly/later-spawned entity like a resurrected
// skeleton, propagated — see SpawnedByCardHelper) to every entity a Troop/Building category
// card's play creates. CardEntityId is that card entity's own id (see CardComponent), NOT a
// CardType — two copies of the same card in a deck are two different card entities, so their
// spawns are tracked independently.
//
// Read by CardReturnSystem (to know when every entity a card spawned has died, so the card
// itself can finally be recycled back into its owner's deck — see CardComponent.Location's
// own doc comment) and by RecallSystem (to find every "pair" troop a multi-spawn card like
// SkeletonsCard/OrcsCard fielded together, so recalling one recalls all of them at once).
public struct SpawnedByCardComponent : IComponent
{
    public ulong CardEntityId;
}
