public enum RenderableType : byte
{
    Capsule = 0,
    Sphere = 1,
    BasicMelee = 2,
    // A second BasicTroopRenderer instance (its own prefab, registered against this type)
    // can be pointed at this to render ranged troops without any renderer code changes.
    BasicRanged = 3,
    SeekingProjectile = 4,
    BasicBuilding = 5,
    RespawnableBuilding = 6,
    Tree = 7,
    PlayerBaseCore = 8,
}

public struct RenderableComponent : IComponent
{
    public RenderableType Type;
}