using UnityEngine;

public static class RandomWalkSystem
{
    public static readonly MultipleComponentSystem Instance = new MultipleComponentSystem(
        new[] { typeof(PositionComponent), typeof(RandomWalkComponent) },
        Execute
    );

    private const float WalkBounds = 50f;

    private static void Execute(uint[] indices, ECS ecs, FlagEventManager flagEvents)
    {
        ref var pos  = ref ecs.GetComponentStore<PositionComponent>().GetComponentByIndex(indices[0]);
        ref var walk = ref ecs.GetComponentStore<RandomWalkComponent>().GetComponentByIndex(indices[1]);

        float dx   = walk.TargetX - pos.X;
        float dy   = walk.TargetY - pos.Y;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);

        if (dist <= walk.ArrivalRadius)
        {
            walk.TargetX = Random.Range(-WalkBounds, WalkBounds);
            walk.TargetY = Random.Range(-WalkBounds, WalkBounds);
        }
        else
        {
            float step = walk.Speed * TickManager.TickInterval;
            pos.PrevX = pos.X;
            pos.PrevY = pos.Y;
            pos.X += dx / dist * step;
            pos.Y += dy / dist * step;
            flagEvents.Add<PositionUpdatedEvent>();
        }
    }
}
