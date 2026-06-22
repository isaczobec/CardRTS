public enum RenderableType : byte
{
    Capsule = 0,
    Sphere = 1,
}

public struct RenderableComponent : IComponent
{
    public RenderableType Type;
}