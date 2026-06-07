public struct PositionComponent : IComponent
{
    public float X;
    public float Y;
    public float PrevX;
    public float PrevY;

    public PositionComponent(float x, float y)
    {
        X = x;
        Y = y;
        PrevX = x;
        PrevY = y;
    }
}
