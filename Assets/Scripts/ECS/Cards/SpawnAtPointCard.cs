// Card kind for "click one ground point, spawn one entity there" cards — today's only
// kind (troops, buildings). See Card.cs for how a future kind (multi-point, entity-
// targeted, ...) would sit alongside this one instead of extending it.
public abstract class SpawnAtPointCard : Card
{
    // Key into IndicatorPrefabRegistry for a prefab that follows the player's mouse
    // (snapped to the ground) while this card is selected/being dragged, previewing where
    // the spawned entity will land — see CardPlacementIndicatorManager. Null/empty means
    // no indicator is shown for this card.
    public virtual string IndicatorPrefabName => null;

    // Called by SpawnAtPointCardPlaySystem when a player plays this card at (x, y).
    // cardEntityId is the card entity that was played (recycled back into the deck
    // afterwards, not deleted), in case an implementation ever needs to read more off it
    // than SpawnAtPointCardPlaySystem already validated.
    public abstract void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y);
}
