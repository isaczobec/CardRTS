public struct RandomWalkComponent : IComponent
{
    public float TargetX;
    public float TargetY;
    public float Speed;
    public float ArrivalRadius;

    public RandomWalkComponent(float speed, float arrivalRadius = 1f)
    {
        TargetX = 0f;
        TargetY = 0f;
        Speed = speed;
        ArrivalRadius = arrivalRadius;
    }
}
