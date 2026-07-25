// Passive Stone-generation building — see SawmillCard for the full shape this mirrors (same
// BuildingCard-style stationary setup, plus ResourceGeneratorCardHelper's distance-based
// 30-100/minute rate).
public class QuarryCard : SpawnAtPointCard
{
    // Matches BuildingCard's own MaxHealth/ActivationDelaySeconds/GoldDropOnDeath.
    private const int MaxHealth = 350;
    private const float ActivationDelaySeconds = 2f;
    private const int GoldDropOnDeath = 20;

    private const float MaxDistanceFromBuilding = 45f;

    public override int ShopGoldCost => 120;

    public override CardType Type => CardType.Quarry;
    public override string Title => "Quarry";
    public override string ImageName => "Quarry";
    public override string Description => "A stationary structure that periodically generates Stone — more, the further it is from your own base.";
    public override string IndicatorPrefabName => "Quarry";

    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = StatsComponent.STAT_NA,
        Range       = StatsComponent.STAT_NA,
        Armor       = BuildingSpawnHelper.Armor,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
        SpellResist = BuildingSpawnHelper.SpellResist,
    };

    // 80 of each of the other two resources — explicit design ask.
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 80,
            Metal = 80
        };

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        BuildingSpawnHelper.AddBuildingComponents(ecs, id, ownerPlayerId, RenderableType.Quarry, MaxHealth,
            (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds));

        ResourceGeneratorCardHelper.AddResourceGenerator(ecs, id, ownerPlayerId, x, y, ResourceType.Stone);

        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Gold = GoldDropOnDeath
        } } );

        return id;
    }
}
