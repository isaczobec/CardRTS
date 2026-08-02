using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// CleaveComponent — mirrors GiantsbaneUpgrade's own always-visible permanent modifier/icon,
// just with a CleaveComponent payload instead. See CleaveSystem for the actual splash-on-hit
// effect.
public class CleaveUpgrade : CardUpgrade
{
    private const float SplashRatio = 0.3f;
    private const float SplashRadius = 7f;

    public override UpgradeType Type => UpgradeType.Cleave;
    public override string Title => "Cleave";
    public override string Description => $"Every hit also deals {SplashRatio * 100f:0}% of the damage dealt to enemies near the target.";
    public override string ImageName => "Cleave";
    public override int ShopGoldCost => 160;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.Cleave,
        });
        ecs.AddComponent(modifier.Id, new CleaveComponent
        {
            SplashRatio = SplashRatio,
            Radius      = SplashRadius,
        });
    };
}
