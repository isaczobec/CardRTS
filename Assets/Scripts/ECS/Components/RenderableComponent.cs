public enum RenderableType : byte
{
    Capsule = 0,
    Sphere = 1,
    BasicMelee = 2,
}

public struct RenderableComponent : IComponent
{
    public RenderableType Type;
}