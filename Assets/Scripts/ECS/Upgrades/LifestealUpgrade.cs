using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// LifestealComponent — mirrors DamageBoostUpgrade's own always-visible permanent
// modifier/icon, just with a LifestealComponent payload instead of StatModifierComponent.
public class LifestealUpgrade : CardUpgrade
{
    private const float LifestealRatio = 0.25f;

    public override UpgradeType Type => UpgradeType.Lifesteal;
    public override string Title => "Lifesteal";
    public override string Description => $"Heals for {LifestealRatio * 100f:0}% of damage dealt.";
    public override string ImageName => "Lifesteal";
    // Reads more as sustain/survivability than raw damage output — filed under Defense.
    public override UpgradeCategory Category => UpgradeCategory.Defense;
    public override int ShopGoldCost => 160;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.Lifesteal,
        });
        ecs.AddComponent(modifier.Id, new LifestealComponent { LifestealRatio = LifestealRatio });
    };
}
