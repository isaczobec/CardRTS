// Passive Wood-generation building — same "stationary, non-AI" shape as BuildingCard (see
// its own doc comment), plus a ResourceGeneratorComponent (see
// ResourceGeneratorCardHelper.AddResourceGenerator) whose rate scales from 30/minute right
// at this player's own base up to 100/minute at the world's center, linearly in between,
// based on how far from that base this Sawmill is actually placed.
public class SawmillCard : SpawnAtPointCard
{
    // Matches BuildingCard's own MaxHealth/ActivationDelaySeconds/GoldDropOnDeath.
    private const int MaxHealth = 350;
    private const float ActivationDelaySeconds = 2f;
    private const int GoldDropOnDeath = 20;

    private const float MaxDistanceFromBuilding = 45f;

    public override int ShopGoldCost => 120;

    public override CardType Type => CardType.Sawmill;
    public override string Title => "Sawmill";
    public override string ImageName => "Sawmill";
    public override string Description => "A stationary structure that periodically generates Wood — more, the further it is from your own base.";
    public override string IndicatorPrefabName => "Sawmill";

    public override float MaxDistanceFromFriendlyBuilding => MaxDistanceFromBuilding;

    // Speed/Range/Damage/AttackSpeed are STAT_NA — this building doesn't move or attack, so
    // those rows shouldn't be shown on the card face at all. Mirrors BuildingCard.
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
            Stone = 80,
            Metal = 80
        };

    public override ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));
        BuildingSpawnHelper.AddBuildingComponents(ecs, id, ownerPlayerId, RenderableType.Sawmill, MaxHealth,
            (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds));

        ResourceGeneratorCardHelper.AddResourceGenerator(ecs, id, ownerPlayerId, x, y, ResourceType.Wood);

        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Gold = GoldDropOnDeath
        } } );

        return id;
    }
}
