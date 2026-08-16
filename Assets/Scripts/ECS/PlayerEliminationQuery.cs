// Shared "is this player eliminated" lookup (PlayerComponent.IsEliminated — see
// PlayerEliminationSystem, the only writer) — mirrors PlayerBaseQuery's own shape. Read from
// ECS.GetInputsForTick (drops an eliminated player's input, server-side and on every client's
// own ClientLocalECS alike) and InputBuffer.ShouldAcceptLocalInput (stops the local player from
// even enqueueing new input once their own base falls).
public static class PlayerEliminationQuery
{
    public static bool IsEliminated(ECS ecs, ushort playerId)
    {
        ComponentStore<PlayerComponent> playerStore = ecs?.GetComponentStore<PlayerComponent>();
        if (playerStore == null) return false;

        bool eliminated = false;
        playerStore.ForEach((ulong id) =>
        {
            if (eliminated) return;
            PlayerComponent player = playerStore.GetComponent(id);
            if (player.PlayerId == playerId) eliminated = player.IsEliminated;
        });
        return eliminated;
    }
}
