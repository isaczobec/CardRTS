using System;

// Mirrors DamageBoostUpgrade exactly — see that class's own doc comment for why a separate
// permanent modifier entity (rather than a direct StatsComponent mutation) is used. Uses the
// new StatModifierComponent.SpellResistAdditiveBonus field (added alongside this upgrade —
// no existing card/upgrade granted Spell Resist before).
public class SpellShieldUpgrade : CardUpgrade
{
    private const float SpellResistAdditiveBonus = 35f;

    public override UpgradeType Type => UpgradeType.SpellShield;
    public override string Title => "Spell Shield";
    public override string Description => $"+{SpellResistAdditiveBonus:0} spell resist.";
    // Matches ModifierIconManager.ResolveStatChange's own auto-derived single-stat image name
    // ("SpellResist" + "Boost") so this one image asset serves both the shop icon and the
    // in-combat buff icon.
    public override string ImageName => "SpellResistBoost";
    public override UpgradeCategory Category => UpgradeCategory.Defense;
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
            SpellResistAdditiveBonus = SpellResistAdditiveBonus,
        });
    };
}
