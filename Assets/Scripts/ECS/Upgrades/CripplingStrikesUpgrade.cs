using System;

public class CripplingStrikesUpgrade : CardUpgrade
{
    private const float SlowRatio = 0.5f;
    private const float DurationSeconds = 2f;

    public override UpgradeType Type => UpgradeType.CripplingStrikes;
    public override string Title => "Crippling Strikes";
    public override string Description => $"Direct hits slow the target by {SlowRatio * 100f:0}% for {DurationSeconds:0} second.";
    public override string ImageName => "CripplingStrikes";
    public override int ShopGoldCost => 140;
    // Explicit design ask — the slow itself doesn't stack (see CripplingStrikesSystem), so
    // only one copy of this upgrade may be equipped on the same card at once.
    public override int MaxStackCount => 1;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.CripplingStrikes,
        });
        ecs.AddComponent(modifier.Id, new CripplingStrikesSourceComponent
        {
            SlowRatio       = SlowRatio,
            DurationSeconds = DurationSeconds,
        });
        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.CripplingStrikes,
        });
    };
}
