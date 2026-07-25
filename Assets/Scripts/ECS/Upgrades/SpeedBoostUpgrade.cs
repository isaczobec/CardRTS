using System;

// Mirrors DamageBoostUpgrade exactly — see that class's own doc comment for why a separate
// permanent modifier entity (rather than a direct StatsComponent mutation) is used.
public class SpeedBoostUpgrade : CardUpgrade
{
    private const float SpeedRatioBonus = 0.20f;

    public override UpgradeType Type => UpgradeType.SpeedBoost;
    public override string Title => "Speed Boost";
    public override string Description => $"+{SpeedRatioBonus * 100f:0}% movement speed.";
    // Matches ModifierIconManager.ResolveStatChange's own auto-derived single-stat image name
    // ("Speed" + "Boost") — also happens to be the same key SpeedBoostCard's own temporary
    // buff already uses, so no new image asset is needed for this one.
    public override string ImageName => "SpeedBoost";
    public override int ShopGoldCost => 70;

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
            SpeedRatioBonus = SpeedRatioBonus,
        });
    };
}
