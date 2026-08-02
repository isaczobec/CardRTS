using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// BuildingDamageBonusComponent — the exact same "+X% damage vs. buildings" primitive
// GoblinSnatcherCard/StoneConstructCard grant innately, just purchasable here instead. See
// BuildingDamageBonusSystem, which already SUMS every active instance targeting the same
// dealer (rather than only applying the first found), so two purchases correctly compose
// into +50% rather than either one overwriting the other.
public class SiegebreakerUpgrade : CardUpgrade
{
    private const float BonusRatio = 0.25f;

    public override UpgradeType Type => UpgradeType.Siegebreaker;
    public override string Title => "Siegebreaker";
    public override string Description => $"Deals {BonusRatio * 100f:0}% more damage against buildings.";
    public override string ImageName => "Siegebreaker";
    public override int ShopGoldCost => 80  ;
    // Explicit design ask — up to 2 copies of this upgrade may be equipped on the same card.
    public override int MaxStackCount => 2;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.BuildingDamage,
        });
        ecs.AddComponent(modifier.Id, new BuildingDamageBonusComponent
        {
            BonusRatio = BonusRatio,
        });
    };
}
