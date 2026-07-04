using UnityEngine;

public static class RandomWalkSystem
{
    public static readonly MultipleComponentSystem Instance = new MultipleComponentSystem(
        new[] { typeof(PositionComponent), typeof(RandomWalkComponent) },
        Execute
    );

    private const float WalkBounds = 50f;

    private static void Execute(ulong entityId, uint[] indices, ECS ecs, FlagEventManager flagEvents)
    {
        ref var pos  = ref ecs.GetComponentStore<PositionComponent>().GetComponentByIndex(indices[0]);
        ref var walk = ref ecs.GetComponentStore<RandomWalkComponent>().GetComponentByIndex(indices[1]);

        float dx   = walk.TargetX - pos.X;
        float dy   = walk.TargetY - pos.Y;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);

        if (dist <= walk.ArrivalRadius)
        {
            walk.TargetX = NextFloat(ref walk.Seed, -WalkBounds, WalkBounds);
            walk.TargetY = NextFloat(ref walk.Seed, -WalkBounds, WalkBounds);
            ecs.Delta.MarkComponentDirty(entityId, typeof(RandomWalkComponent));
        }
        else
        {
            float step = walk.Speed * TickManager.TickInterval;
            pos.X += dx / dist * step;
            pos.Y += dy / dist * step;
            flagEvents.Add(new PositionUpdatedEvent());
            ecs.Delta.MarkComponentDirty(entityId, typeof(PositionComponent));
        }
    }

    // Xorshift32 — period 2^32-1, never produces 0 from a non-zero seed.
    private static float NextFloat(ref uint seed, float min, float max)
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return min + (seed / (float)uint.MaxValue) * (max - min);
    }
}
