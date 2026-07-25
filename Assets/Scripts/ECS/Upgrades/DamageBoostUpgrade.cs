using System;

// Example upgrade proving the CardUpgrade pattern end-to-end — grants a permanent +20%
// damage stat modifier (ModifierComponent + StatModifierComponent, see StatModifierSystem)
// to whatever entity the upgraded SpawnAtPointCard just spawned. A separate modifier entity
// rather than a direct StatsComponent mutation, so it composes correctly with any other
// damage modifier instead of clobbering the raw stat (see StatModifierComponent's own doc
// comment on how multiple ratio bonuses combine). TicksRemaining = int.MaxValue means
// ModifierSystem never counts it down/expires it, and no ActivatableComponent is added, so
// StatModifierSystem (via ModifierQuery.IsActive) treats it as active immediately. Harmless
// to grant unconditionally even on an entity with no real Damage stat (e.g. a building) —
// StatsQuery bypasses modifiers entirely for a STAT_NA base value, so it's simply never
// applied there.
public class DamageBoostUpgrade : CardUpgrade
{
    private const float DamageRatioBonus = 0.2f;

    public override UpgradeType Type => UpgradeType.DamageBoost;
    public override string Title => "Damage Boost";
    public override string Description => $"+{DamageRatioBonus * 100f:0}% damage.";
    public override string ImageName => "DamageBoost";
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
            DamageRatioBonus = DamageRatioBonus,
        });
    };
}
