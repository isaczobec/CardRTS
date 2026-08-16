public struct PlayerComponent : IComponent
{
    public ushort PlayerId;

    // Set once (see PlayerEliminationSystem) the instant this player's base dies — never
    // cleared. Ordinary component data (not per-instance derived state), so it replicates to
    // every client via the normal delta/reconciliation pipeline just like any other component
    // field, including to a client that only starts observing/reconciling after the
    // elimination already happened.
    public bool IsEliminated;
}
