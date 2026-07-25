using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// GiantsbaneComponent — mirrors DamageBoostUpgrade's own always-visible permanent
// modifier/icon, just with a GiantsbaneComponent payload instead of StatModifierComponent.
public class GiantsbaneUpgrade : CardUpgrade
{
    private const int PeriodHits = 4;
    private const float BonusDamageMaxHealthRatio = 0.13f;

    public override UpgradeType Type => UpgradeType.Giantsbane;
    public override string Title => "Giantsbane";
    public override string Description => $"Every {PeriodHits}th hit deals an additional {BonusDamageMaxHealthRatio * 100f:0}% of the target's max health as bonus damage.";
    public override string ImageName => "Giantsbane";
    public override int ShopGoldCost => 160;
    // Explicit design ask — up to 2 copies of this upgrade may be equipped on the same card.
    public override int MaxStackCount => 2;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.Giantsbane,
        });
        ecs.AddComponent(modifier.Id, new GiantsbaneComponent
        {
            PeriodHits                = PeriodHits,
            // Don't proc on the very first hit — wait out a full PeriodHits first, matching
            // OnHitScheduleComponent.HitsUntilProc's own "initialize to PeriodHits" convention.
            HitsUntilProc             = PeriodHits,
            BonusDamageMaxHealthRatio = BonusDamageMaxHealthRatio,
        });
    };
}
