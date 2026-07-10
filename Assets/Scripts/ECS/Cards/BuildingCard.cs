// Buildings are stationary, non-AI entities — no MovableComponent (they never move) and
// no AI component (they don't act), so they don't fit TroopCardHelper's shape. Built
// inline instead, mirroring what used to be SpawnTroopSystem.SpawnBuilding.
public class BuildingCard : Card
{
    private const int MaxHealth = 300;
    private const int Armor = 5;
    private const float BlockRadius = 3f;
    private const float ActivationDelaySeconds = 2f;

    public override CardType Type => CardType.Building;
    public override string Title => "Building";
    public override string ImageName => "Building";
    public override string Description => "A stationary structure that blocks nearby idle troops from standing on it.";
    public override StatsComponent? DisplayStats => BuildStats();

    private static StatsComponent BuildStats() => new StatsComponent
    {
        MaxHealth = MaxHealth,
        Armor     = Armor,
        // Speed/Range/Damage/AttackSpeed left at 0 — buildings don't move or attack.
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
