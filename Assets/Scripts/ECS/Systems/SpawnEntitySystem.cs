using System.Collections.Generic;
using UnityEngine;

public static class SpawnEntitySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<SpawnEntityInput> inputs = ecs.GetInputsForTick<SpawnEntityInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (var _ in inputs)
        {
            EntityHandle entity = ecs.CreateEntity();
            ecs.AddComponent(entity.Id, new PositionComponent());
            ecs.AddComponent(entity.Id, new RandomWalkComponent(speed: 5f, arrivalRadius: 2.0f));
        }
    }
}
