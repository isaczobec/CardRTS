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
    AoeSpell = 9,
    Rock = 10,
    Ore = 11,
    SkillshotProjectile = 12,
    SoulstoneNodeSmall = 13,
    SoulstoneNodeMedium = 14,
    SoulstoneNodeLarge = 15,
    Gem = 16,
    IronKnight = 17,
    Ranger = 18,
    FastSkillshotProjectile = 19,
    IceMan = 20,
    IceProjectile = 21,
    FireMan = 22,
    FireProjectile = 23,
}

public struct RenderableComponent : IComponent
{
    public RenderableType Type;
}