public struct RandomWalkComponent : IComponent
{
    public float TargetX;
    public float TargetY;
    public float Speed;
    public float ArrivalRadius;
    public uint Seed;

    public RandomWalkComponent(float speed, float arrivalRadius = 1f, uint seed = 1)
    {
        TargetX = 0f;
        TargetY = 0f;
        Speed = speed;
        ArrivalRadius = arrivalRadius;
        Seed = seed == 0 ? 1u : seed;
    }
}
