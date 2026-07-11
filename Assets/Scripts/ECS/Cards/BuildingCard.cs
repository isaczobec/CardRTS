// Buildings are stationary, non-AI entities — no MovableComponent (they never move) and
// no AI component (they don't act), so they don't fit TroopCardHelper's shape. Built
// inline instead, mirroring what used to be SpawnTroopSystem.SpawnBuilding.
public class BuildingCard : Card
{
    private const int MaxHealth = 300;
    private const int Armor = 5;
    private const float BlockRadius = 3f;
    private const float ActivationDelaySeconds = 2f;

    private const int WoodCost = 5;
    private const int StoneCost = 3;

    public override CardType Type => CardType.Building;
    public override string Title => "Building";
    public override string ImageName => "Building";
    public override string Description => "A stationary structure that blocks nearby idle troops from standing on it.";

    // Speed/Range/Damage/AttackSpeed are STAT_NA (not just 0) here — buildings don't move
    // or attack, so those rows shouldn't be shown on the card face at all.
    public override StatsComponent DefaultStats => new StatsComponent
    {
        MaxHealth   = MaxHealth,
        Speed       = StatsComponent.STAT_NA,
        Range       = StatsComponent.STAT_NA,
        Armor       = Armor,
        Damage      = StatsComponent.STAT_NA,
        AttackSpeed = StatsComponent.STAT_NA,
    };

    public override ResourceCost Cost => new ResourceCost { Wood = WoodCost, Stone = StoneCost };

    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth = MaxHealth,
        Armor     = Armor,
        // Speed/Range/Damage/AttackSpeed left at 0 (not STAT_NA) — this is the actual
        // gameplay component added to the spawned entity below, where 0 correctly means
        // "stationary"/"never attacks", unlike DefaultStats which is UI-display-only.
    };

    public override void OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));

        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId     = ownerPlayerId,
            _ticksUntilActive = (ulong)TickManager.SecondsToTicks(ActivationDelaySeconds),
        });

        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.BasicBuilding });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = ownerPlayerId });

        ecs.AddComponent(id, BuildStats());
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = MaxHealth });
        ecs.AddComponent(id, new BuildingComponent { BlockRadius = BlockRadius });
    }
}
