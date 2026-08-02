using System;

// Grants a permanent modifier entity (ModifierComponent.TicksRemaining = int.MaxValue,
// targeting whatever entity the upgraded SpawnAtPointCard just spawned) carrying a
// BruiserComponent — mirrors DamageBoostUpgrade's own always-visible permanent
// modifier/icon, just with a BruiserComponent payload instead of StatModifierComponent.
public class BruiserUpgrade : CardUpgrade
{
    private const float DeferralRatio = 0.6f;
    private const float DrainPerSecond = 15f;

    public override UpgradeType Type => UpgradeType.Bruiser;
    public override string Title => "Bruiser";
    public override string Description =>
        $"Reduces incoming damage by {DeferralRatio * 100f:0}%, but that damage is instead taken over time at {DrainPerSecond:0}/second. " +
        "The banked damage clears if this troop goes 5 seconds without taking a hit.";
    public override string ImageName => "Bruiser";
    public override UpgradeCategory Category => UpgradeCategory.Defense;
    public override int ShopGoldCost => 185;
    public override int MaxStackCount => 1;

    public override Action<ulong, ECS> OnSpawnAtPointCardPlayed => (entityId, ecs) =>
    {
        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = entityId,
            TicksRemaining = int.MaxValue,
            ModifierID     = ModifierID.Bruiser,
        });
        ecs.AddComponent(modifier.Id, new BruiserComponent
        {
            DeferralRatio  = DeferralRatio,
            DrainPerSecond = DrainPerSecond,
        });
    };
}
