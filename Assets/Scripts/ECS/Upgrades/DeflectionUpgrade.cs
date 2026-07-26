using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// DeflectionComponent — mirrors DamageBoostUpgrade's own always-visible permanent
// modifier/icon, just with a DeflectionComponent payload instead of StatModifierComponent.
public class DeflectionUpgrade : CardUpgrade
{
    private const float MinRange = 5f;
    private const float MaxRange = 20f;
    private const float MaxReductionRatio = 0.45f;

    public override UpgradeType Type => UpgradeType.Deflection;
    public override string Title => "Deflection";
    public override string Description =>
        $"Reduces incoming damage the further away the attacker is — no reduction within {MinRange:0} tiles, scaling up to {MaxReductionRatio * 100f:0}% at {MaxRange:0}+ tiles.";
    public override string ImageName => "Deflection";
    public override int ShopGoldCost => 165;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.Deflection,
        });
        ecs.AddComponent(modifier.Id, new DeflectionComponent
        {
            MinRange          = MinRange,
            MaxRange          = MaxRange,
            MaxReductionRatio = MaxReductionRatio,
        });
    };
}
