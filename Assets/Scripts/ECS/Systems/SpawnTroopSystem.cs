using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Server-only. Reads SpawnTroopInput each tick and creates a troop entity whose
/// tickToBecomeActive is 20 ticks ahead, giving the delta time to reach the client
/// before the troop activates.
/// </summary>
public static class SpawnTroopSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const float DefaultSpeed = 10f;

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<SpawnTroopInput> inputs = ecs.GetInputsForTick<SpawnTroopInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (var input in inputs)
        {
            EntityHandle entity = ecs.CreateEntity();

            ecs.AddComponent(entity.Id, new PositionComponent(input.X, input.Y));

            ecs.AddComponent(entity.Id, new TroopComponent
            {
                OwnerPlayerId      = input.ClientId,
                tickToBecomeActive = ecs.CurrentSimulationTick + 20,
            });

            DebugLogger.Log($"Spawned troop entity {entity.Id} for player {input.ClientId} at ({input.X}, {input.Y})");
            ecs.AddComponent(entity.Id, new RenderableComponent
            {
                Type = RenderableType.Capsule,
            });

            ecs.AddComponent(entity.Id, new SelectableComponent
            {
                OwnerPlayerId = input.ClientId,
            });

            ecs.AddComponent(entity.Id, new MovableComponent
            {
                DestinationX = input.X,
                DestinationY = input.Y,
                Speed = DefaultSpeed,
            });
        }
    }
}
