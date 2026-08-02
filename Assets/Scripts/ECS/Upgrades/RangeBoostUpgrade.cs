using System;

// Mirrors DamageBoostUpgrade exactly — see that class's own doc comment for why a separate
// permanent modifier entity (rather than a direct StatsComponent mutation) is used.
public class RangeBoostUpgrade : CardUpgrade
{
    private const float RangeRatioBonus = 0.27f;

    public override UpgradeType Type => UpgradeType.RangeBoost;
    public override string Title => "Range Boost";
    public override string Description => $"+{RangeRatioBonus * 100f:0}% range.";
    // Matches ModifierIconManager.ResolveStatChange's own auto-derived single-stat image name
    // ("Range" + "Boost") so this one image asset serves both the shop icon and the in-combat
    // buff icon.
    public override string ImageName => "RangeBoost";
    public override int ShopGoldCost => 70;
    // Explicit design ask — up to 3 copies of any basic stat upgrade may be equipped on the
    // same card.
    public override int MaxStackCount => 3;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.StatChange,
        });
        ecs.AddComponent(modifier.Id, new StatModifierComponent
        {
            RangeRatioBonus = RangeRatioBonus,
        });
    };
}
