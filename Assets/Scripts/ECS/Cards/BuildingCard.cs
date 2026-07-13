// Buildings are stationary, non-AI entities — no MovableComponent (they never move) and
// no AI component (they don't act), so they don't fit TroopCardHelper's shape. Built
// inline instead, mirroring what used to be SpawnTroopSystem.SpawnBuilding.
public class BuildingCard : SpawnAtPointCard
{
    private const int MaxHealth = 300;
    private const float ActivationDelaySeconds = 2f;

    private const int WoodCost = 5;
    private const int StoneCost = 3;

    // A bit more generous than troops — buildings are how you expand toward new
    // territory, so they shouldn't be stuck only ever hugging existing ones.
    private const float MaxDistanceFromBuilding = 25f;

    public override CardType Type => CardType.Building;
    public override string Title => "Building";
    public override string ImageName => "Building";
    public override string Description => "A stationary structure that blocks nearby idle troops from standing on it.";
    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    // Speed/Range/Damage/AttackSpeed are STAT_NA (not just 0) here — buildings don't move
    // or attack, so those rows shouldn't be shown on the card face at all.
    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = StatsComponent.STAT_NA,
        Range       = StatsComponent.STAT_NA,
        Armor       = BuildingSpawnHelper.Armor,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
    };

    public override ResourceCost Cost => new ResourceCost { Wood = WoodCost, Stone = StoneCost };

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        BuildingSpawnHelper.AddBuildingComponents(ecs, id, ownerPlayerId, RenderableType.BasicBuilding, MaxHealth,
            (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds));
    }
}
