using System.Collections.Generic;

/// <summary>
/// Server-only. Reads SpawnTroopInput each tick and creates a troop entity whose
/// tickToBecomeActive is 20 ticks ahead, giving the delta time to reach the client
/// before the troop activates.
/// </summary>
public static class SpawnTroopSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

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

            ecs.AddComponent(entity.Id, new RenderableComponent
            {
                Type = RenderableType.Capsule,
            });
        }
    }
}
