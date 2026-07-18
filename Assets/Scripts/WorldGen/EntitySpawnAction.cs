using System;

// IWorldGenAction that creates a single entity at (X, Y) and delegates the rest of its
// component setup to a Spawner lambda. Enqueue via WorldGenHandler.EnqueueAction during a
// WorldGenFeature.Generate call; the action runs on the server after all tiles are filled.
//
// SpawnTree is a ready-made Spawner for a neutral respawnable tree resource.
public class EntitySpawnAction : IWorldGenAction
{
    // Scaled to match BasicMeleeTroopCard's rebalance baseline (3x health) — preserves
    // Tree/Rock/Ore's old relative hardiness vs. a troop (Tree was exactly as tanky as a
    // troop, Rock/Ore 1.5x as tanky; both ratios are unchanged here).
    private const int   TreeMaxHealth     = 300;
    private const float TreeBlockRadius   = 1f;
    private const float TreeRespawnSeconds = 120f;
    private const float TreeSelectionScale = 2f;

    private const int   RockMaxHealth      = 450;
    private const float RockBlockRadius    = 1f;
    private const float RockRespawnSeconds = 120f;
    private const float RockSelectionScale = 2f;

    private const int   OreMaxHealth       = 450;
    private const float OreBlockRadius     = 1f;
    private const float OreRespawnSeconds  = 150f;
    private const float OreSelectionScale  = 2f;

    public float X;
    public float Y;

    // Called with the new entity's id and the ECS after PositionComponent has been added.
    public Action<ulong, ECS> Spawner;

    // Neutral respawnable tree: active after the first tick, health restores after 30 s.
    public static readonly Action<ulong, ECS> SpawnTree = (id, ecs) =>
    {
        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID,
            IsPhysicalTroop = false,
        });
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = 1,   // activates on the first tick so the renderer fires OnEntityActivated
            InitialTicksUntilActive = 1,
        });
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.Tree });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID, Scale = TreeSelectionScale });
        ecs.AddComponent(id, new StatsComponent { MaxHealth = TreeMaxHealth });
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = TreeMaxHealth });
        ecs.AddComponent(id, new BuildingComponent { BlockRadius = TreeBlockRadius, CardPlayRangeMultiplier = 1f });
        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Wood = 20
        } } );
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(TreeRespawnSeconds),
        });
    };

    // Neutral respawnable rock: drops Stone.
    public static readonly Action<ulong, ECS> SpawnRock = (id, ecs) =>
    {
        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID,
            IsPhysicalTroop = false,
        });
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = 1,
            InitialTicksUntilActive = 1,
        });
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.Rock });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID, Scale = RockSelectionScale });
        ecs.AddComponent(id, new StatsComponent { MaxHealth = RockMaxHealth });
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = RockMaxHealth });
        ecs.AddComponent(id, new BuildingComponent { BlockRadius = RockBlockRadius, CardPlayRangeMultiplier = 1f });
        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Stone = 20
        } } );
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(RockRespawnSeconds),
        });
    };

    // Neutral respawnable ore deposit: drops Metal.
    public static readonly Action<ulong, ECS> SpawnOre = (id, ecs) =>
    {
        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID,
            IsPhysicalTroop = false,
        });
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = 1,
            InitialTicksUntilActive = 1,
        });
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.Ore });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID, Scale = OreSelectionScale });
        ecs.AddComponent(id, new StatsComponent { MaxHealth = OreMaxHealth });
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = OreMaxHealth });
        ecs.AddComponent(id, new BuildingComponent { BlockRadius = OreBlockRadius, CardPlayRangeMultiplier = 1f });
        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Metal = 20
        } } );
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(OreRespawnSeconds),
        });
    };

    public void Execute(ECS ecs)
    {
        EntityHandle entity = ecs.CreateEntity();
        ecs.AddComponent(entity.Id, new PositionComponent(X, Y));
        Spawner?.Invoke(entity.Id, ecs);
    }
}
