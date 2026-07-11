// One instance per distinct card definition in the game — not one per card entity. Cards
// are stateless: all per-instance state (whose deck/hand it's in, etc.) lives on the ECS
// side in CardComponent, and whatever OnPlayed creates. See CardRegistry for the
// CardType -> Card lookup CardPlaySystem uses to dispatch a played card.
// Will grow fields/methods for displaying the card in hand (name, description, icon...).
public abstract class Card
{
    public abstract CardType Type { get; }

    public abstract string Title { get; }

    // Key into ImageRegistry for this card's artwork.
    public abstract string ImageName { get; }

    public abstract string Description { get; }

    // Stats to show on the card face (CardGameObject.BuildCard). Fields that don't apply
    // to this card (e.g. Speed on a building) should be set to StatsComponent.STAT_NA so
    // the UI hides that row instead of showing "0".
    public abstract StatsComponent DefaultStats { get; }

    // Resource price to play this card, checked by CardPlaySystem before OnPlayed runs and
    // deducted via ResourceHelper.Spend. A field of 0 means the card doesn't cost that
    // resource at all (and its row is hidden on the card face).
    public abstract ResourceCost Cost { get; }

    // Called by CardPlaySystem when a player plays this card at (x, y). cardEntityId is
    // the card entity that was played (recycled back into the deck afterwards, not
    // deleted), in case an implementation ever needs to read more off it than
    // CardPlaySystem already validated.
    public abstract void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y);
}
