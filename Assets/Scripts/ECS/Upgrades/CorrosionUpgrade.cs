using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// CorrosionSourceComponent — mirrors GiantsbaneUpgrade/CleaveUpgrade's own always-visible
// permanent modifier shape. See CorrosionSystem for the actual on-hit debuff/armor-reduction
// effect this grants the wielder.
public class CorrosionUpgrade : CardUpgrade
{
    private const int MaxStacks = 4;
    private const float MaxArmorReduction = 50f;
    private const float DurationSeconds = 7f;

    public override UpgradeType Type => UpgradeType.Corrosion;
    public override string Title => "Corrosion";
    public override string Description => $"Hits corrode the target's armor, stacking up to {MaxStacks} times (-{MaxArmorReduction:0} armor at max stacks) for {DurationSeconds:0} seconds, refreshed on every hit.";
    public override string ImageName => "Corrosion";
    public override int ShopGoldCost => 120;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
        });
        ecs.AddComponent(modifier.Id, new CorrosionSourceComponent
        {
            MaxStacks         = MaxStacks,
            MaxArmorReduction = MaxArmorReduction,
            DurationSeconds   = DurationSeconds,
        });
    };
}
