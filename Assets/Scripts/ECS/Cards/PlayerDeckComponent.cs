// Added to a player's own entity (alongside PlayerComponent). Tracks that player's deck
// as a FIFO queue of card entities: DeckHeadId is drawn next, DeckTailId is where a
// played card is re-inserted, and the cards between them are linked via
// CardComponent.NextInDeckId. See DeckHelper for the queue operations and DeckSystem for
// the draw-into-hand cooldown that consumes DeckHeadId over time.
public struct PlayerDeckComponent : IComponent
{
    // Entity ID of the card at the top of the deck (drawn next), or 0 if the deck is
    // currently empty.
    public ulong DeckHeadId;

    // Entity ID of the card at the back of the deck (where a played card is re-inserted),
    // or 0 if the deck is currently empty.
    public ulong DeckTailId;

    // 0 = not currently counting down to a draw. Otherwise ticks remaining until the next
    // card is drawn from the deck into hand — see DeckSystem.
    public int DrawCooldownTicksRemaining;
}
