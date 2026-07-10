// One instance per distinct card definition in the game — not one per card entity. Cards
// are stateless: all per-instance state (whose deck/hand it's in, etc.) lives on the ECS
// side in CardComponent, and whatever OnPlayed creates. See CardRegistry for the
// CardType -> Card lookup CardPlaySystem uses to dispatch a played card.
// Will grow fields/methods for displaying the card in hand (name, description, icon...).
public abstract class Card
{
    public abstract CardType Type { get; }

    // Called by CardPlaySystem when a player plays this card at (x, y). cardEntityId is
    // the (about to be deleted) card entity that was played, in case an implementation
    // ever needs to read more off it than CardPlaySystem already validated.
    public abstract void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y);
}
