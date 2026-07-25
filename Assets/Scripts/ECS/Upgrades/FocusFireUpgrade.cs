using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying both a
// FocusFireComponent (bookkeeping) and a StatModifierComponent (the real, initially-zero
// attack-speed effect FocusFireSystem updates as stacks change) — created upfront rather than
// lazily on first hit, since the bookkeeping needs a home from the start. Mirrors
// DamageBoostUpgrade's own always-visible permanent modifier/icon.
public class FocusFireUpgrade : CardUpgrade
{
    private const int MaxStacks = 8;
    private const float AttackSpeedRatioBonusPerStack = 0.07f;

    public override UpgradeType Type => UpgradeType.FocusFire;
    public override string Title => "Focus Fire";
    public override string Description => $"Dealing damage to the same target grants +{AttackSpeedRatioBonusPerStack * 100f:0}% attack speed, stacking up to {MaxStacks} times. Hitting a different target resets the stacks.";
    public override string ImageName => "FocusFire";
    public override int ShopGoldCost => 160;
    // Explicit design ask — doesn't stack; only one copy of this upgrade may be equipped on
    // the same card at once.
    public override int MaxStackCount => 1;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.FocusFire,
        });
        ecs.AddComponent(modifier.Id, new FocusFireComponent
        {
            MaxStacks                     = MaxStacks,
            AttackSpeedRatioBonusPerStack = AttackSpeedRatioBonusPerStack,
        });
        ecs.AddComponent(modifier.Id, new StatModifierComponent());
    };
}
