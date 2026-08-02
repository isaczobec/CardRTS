// Passive Metal-generation building — see SawmillCard for the full shape this mirrors (same
// BuildingCard-style stationary setup, plus ResourceGeneratorCardHelper's distance-based
// 16.5-55/minute rate).
public class MineCard : SpawnAtPointCard
{
    // Matches BuildingCard's own MaxHealth/ActivationDelaySeconds/GoldDropOnDeath.
    private const int MaxHealth = 350;
    private const float ActivationDelaySeconds = 2f;
    private const int GoldDropOnDeath = 20;

    private const float MaxDistanceFromBuilding = 45f;

    public override int ShopGoldCost => 168; // 40% more expensive (explicit design ask), from 120

    public override CardType Type => CardType.Mine;
    public override CardCategory Category => CardCategory.Building;
    public override string Title => "Mine";
    public override string ImageName => "Mine";
    public override string Description => "A stationary structure that periodically generates Metal — more, the further it is from your own base.";
    public override string IndicatorPrefabName => "Mine";

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

    // 40% more expensive (explicit design ask), from 80 of each of the other two resources.
    public override ResourceCost Cost => new ResourceCost
        {
            Wood = 112,
            Stone = 112
        };

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        BuildingSpawnHelper.AddBuildingComponents(ecs, id, ownerPlayerId, RenderableType.Mine, MaxHealth,
            (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds));

        ResourceGeneratorCardHelper.AddResourceGenerator(ecs, id, ownerPlayerId, x, y, ResourceType.Metal);

        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Gold = GoldDropOnDeath
        } } );

        return id;
    }
}
