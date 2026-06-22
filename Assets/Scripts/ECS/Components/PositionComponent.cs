public struct PositionComponent : IComponent
{
    public float X;
    public float Y;

    public ushort TileX => (ushort)X;
    public ushort TileY => (ushort)Y;

    public PositionComponent(float x, float y)
    {
        X = x;
        Y = y;
    }
}
