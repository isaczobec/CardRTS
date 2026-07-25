using System;

// Mirrors DamageBoostUpgrade exactly — see that class's own doc comment for why a separate
// permanent modifier entity (rather than a direct StatsComponent mutation) is used.
// AttackSpeedRatioBonus is negated here — the underlying stat is a tick PERIOD (lower =
// faster attacks), so a mechanically-negative ratio bonus reads as a positive "+20% Attack
// Speed" buff for display purposes (see ModifierIconManager.ResolveStatChange's own comment
// on this).
public class AttackSpeedBoostUpgrade : CardUpgrade
{
    private const float DisplayedAttackSpeedBonus = 0.20f;

    public override UpgradeType Type => UpgradeType.AttackSpeedBoost;
    public override string Title => "Attack Speed Boost";
    public override string Description => $"+{DisplayedAttackSpeedBonus * 100f:0}% attack speed.";
    // Matches ModifierIconManager.ResolveStatChange's own auto-derived single-stat image name
    // ("AttackSpeed" + "Boost") so this one image asset serves both the shop icon and the
    // in-combat buff icon.
    public override string ImageName => "AttackSpeedBoost";
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
            AttackSpeedRatioBonus = -DisplayedAttackSpeedBonus,
        });
    };
}
