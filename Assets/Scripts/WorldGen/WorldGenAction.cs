using System;

// Convenience IWorldGenAction that wraps an Action<ECS> lambda, so features don't need
// to declare a full class just to spawn a few entities.
//
// Usage:
//   handler.EnqueueAction(new WorldGenAction(ecs => {
//       EntityHandle e = ecs.CreateEntity();
//       ecs.AddComponent(e.Id, new PositionComponent(x, y));
//       // ...
//   }));
public class WorldGenAction : IWorldGenAction
{
    private readonly Action<ECS> _action;

    public WorldGenAction(Action<ECS> action)
    {
        _action = action;
    }

    public void Execute(ECS ecs) => _action(ecs);
}
