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
    GoblinSnatcher = 24,
    StoneConstruct = 25,
    Skeleton = 26,
    EphemeralSkeleton = 27,
    Stalker = 28,
    AoeRoot = 29,
    SantaClaus = 30,
    PresentProjectile = 31,
    Cannon = 32,
    CannonProjectile = 33,
    Missile = 34,
    MissileSilo = 35,
    Pirate = 36,
    Hook = 37,
    Sawmill = 38,
    Quarry = 39,
    Mine = 40,
    HealerGuardian = 41,
    HealerGuardianProjectile = 42,
    ShadowAngel = 43,
    ShadowAngelProjectile = 44,
    ConstructionWorker = 45,
    StrategyConsultant = 46,
    BallisticMissileMarker = 47,
    MassiveSleepingDraught = 48,
    Tornado = 49,
    Orc = 50,
    KineticKnight = 51,
    // VengefulSpiritsUpgrade's proc projectile — register a SeekingProjectileRenderer
    // instance against this in the Inspector, same as every other pooled seeking-projectile
    // kind (IceProjectile, FireProjectile, ...).
    VengefulSpirit = 52,
    // Neutral gold crate (see EntitySpawnAction.SpawnGoldCrate/GoldCrateFeature) — register a
    // renderer (e.g. RespawnableBuildingRenderer, same as the SoulstoneNode/Gem entries) with
    // its own crate prefab in the Inspector.
    GoldCrate = 53,
    // Capturable objective building (see EntitySpawnAction.AddCapturableBuildingComponents/
    // CapturableBuildingFeature/CapturableBuildingSystem) — register a renderer with its own
    // prefab in the Inspector. TroopComponent/SelectableComponent.OwnerPlayerId flips from
    // neutral to whoever captures it, read live (not cached) by every ownership-aware
    // renderer/UI element already in the codebase, so the neutral -> owned transition needs
    // no special-casing on the rendering side.
    CapturableBuilding = 54,
    // PurpleWizardCard's own troop — register a renderer with its own prefab in the
    // Inspector, same as every other troop card's RenderableType entry.
    PurpleWizard = 55,
    // PurpleWizardCard's Gravity Well ability telegraph (see AbilityManager.
    // BuildGravityWellAbility) — a ground disc sized from StatsComponent.Range, same shape
    // as AoeRoot/MassiveSleepingDraught; register AoeSpellRenderer (or a copy) with its own
    // prefab/material in the Inspector.
    GravityWell = 56,
}

public struct RenderableComponent : IComponent
{
    public RenderableType Type;
}