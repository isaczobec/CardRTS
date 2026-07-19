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
    private const int   TreeMaxHealth     = 250;
    private const float TreeBlockRadius   = 1f;
    private const float TreeRespawnSeconds = 300f;
    private const float TreeSelectionScale = 2f;

    private const int   RockMaxHealth      = 270;
    private const float RockBlockRadius    = 1f;
    private const float RockRespawnSeconds = 300f;
    private const float RockSelectionScale = 2f;

    private const int   OreMaxHealth       = 270;
    private const float OreBlockRadius     = 1f;
    private const float OreRespawnSeconds  = 300f;
    private const float OreSelectionScale  = 2f;

    // Neutral soulstone resource nodes (see SoulstoneClusterFeature) — dead from the moment
    // they're spawned, each with its own long "grace period" respawn timer, ramping down to
    // a shared shorter steady-state timer after each one's first respawn (see
    // RespawnCooldownRampComponent/RespawnCooldownRampSystem).
    private const int   SoulstoneSmallMaxHealth  = 800;
    private const int   SoulstoneMediumMaxHealth = 550;
    private const int   SoulstoneLargeMaxHealth  = 700;
    private const float SoulstoneBlockRadius     = 1f;
    private const float SoulstoneSelectionScale  = 2f;

    private const float SoulstoneSmallInitialRespawnSeconds  = 5f  * 60f;
    private const float SoulstoneMediumInitialRespawnSeconds = 10f * 60f;
    private const float SoulstoneLargeInitialRespawnSeconds  = 15f * 60f;
    private const float SoulstoneSteadyStateRespawnSeconds   = 5f  * 60f;

    // Neutral gem deposit (see GemClusterFeature) — spawned alive, same shape as
    // Tree/Rock/Ore, just dropping Gems instead.
    private const int   GemMaxHealth      = 700;
    private const float GemBlockRadius    = 1f;
    private const float GemRespawnSeconds = 3f * 60f + 30f; // 3:30
    private const float GemSelectionScale = 2f;
    private const int   GemDropAmount     = 10;

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
            Wood = 12
        } } );
        ecs.AddComponent(id, new ResourceProductionOnDeathComponent { MainResourceType = ResourceType.Wood });
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(TreeRespawnSeconds),
            ShowTimer = true,
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
            Stone = 12
        } } );
        ecs.AddComponent(id, new ResourceProductionOnDeathComponent { MainResourceType = ResourceType.Stone });
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(RockRespawnSeconds),
            ShowTimer = true,
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
            Metal = 12
        } } );
        ecs.AddComponent(id, new ResourceProductionOnDeathComponent { MainResourceType = ResourceType.Metal });
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(OreRespawnSeconds),
            ShowTimer = true,
        });
    };

    // Neutral soulstone resource node: dead from the moment it's spawned (unlike
    // Tree/Rock/Ore, which are spawned alive) — TicksUntilRespawn is seeded with the same
    // value as CooldownTicks so it starts counting down immediately. Drops a single
    // Soulstone on every death, including this initial one.
    private static void SpawnSoulstoneNode(ulong id, ECS ecs, RenderableType type, int maxHealth,
        float initialRespawnSeconds, float steadyStateRespawnSeconds)
    {
        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID,
            IsPhysicalTroop = false,
            IsDead = true,
        });
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = 1,   // activates on the first tick so the renderer fires OnEntityActivated
            InitialTicksUntilActive = 1,
        });
        ecs.AddComponent(id, new RenderableComponent { Type = type });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID, Scale = SoulstoneSelectionScale });
        ecs.AddComponent(id, new StatsComponent { MaxHealth = maxHealth });
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = 0 });
        ecs.AddComponent(id, new BuildingComponent { BlockRadius = SoulstoneBlockRadius, CardPlayRangeMultiplier = 1f });
        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Soulstones = 1
        } });

        ulong initialCooldownTicks = (ulong)TickManager.SecondsToTicks(initialRespawnSeconds);
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks     = initialCooldownTicks,
            TicksUntilRespawn = initialCooldownTicks,
            ShowTimer         = true,
        });
        ecs.AddComponent(id, new RespawnCooldownRampComponent
        {
            CooldownTicksAfterFirstRespawn = (ulong)TickManager.SecondsToTicks(steadyStateRespawnSeconds),
        });
    }

    public static readonly Action<ulong, ECS> SpawnSoulstoneNodeSmall = (id, ecs) =>
        SpawnSoulstoneNode(id, ecs, RenderableType.SoulstoneNodeSmall, SoulstoneSmallMaxHealth,
            SoulstoneSmallInitialRespawnSeconds, SoulstoneSteadyStateRespawnSeconds);

    public static readonly Action<ulong, ECS> SpawnSoulstoneNodeMedium = (id, ecs) =>
        SpawnSoulstoneNode(id, ecs, RenderableType.SoulstoneNodeMedium, SoulstoneMediumMaxHealth,
            SoulstoneMediumInitialRespawnSeconds, SoulstoneSteadyStateRespawnSeconds);

    public static readonly Action<ulong, ECS> SpawnSoulstoneNodeLarge = (id, ecs) =>
        SpawnSoulstoneNode(id, ecs, RenderableType.SoulstoneNodeLarge, SoulstoneLargeMaxHealth,
            SoulstoneLargeInitialRespawnSeconds, SoulstoneSteadyStateRespawnSeconds);

    // Neutral respawnable gem deposit: drops Gems.
    public static readonly Action<ulong, ECS> SpawnGemDeposit = (id, ecs) =>
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
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.Gem });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID, Scale = GemSelectionScale });
        ecs.AddComponent(id, new StatsComponent { MaxHealth = GemMaxHealth });
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = GemMaxHealth });
        ecs.AddComponent(id, new BuildingComponent { BlockRadius = GemBlockRadius, CardPlayRangeMultiplier = 1f });
        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Gems = GemDropAmount
        } } );
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(GemRespawnSeconds),
            ShowTimer = true,
        });
    };

    public void Execute(ECS ecs)
    {
        EntityHandle entity = ecs.CreateEntity();
        ecs.AddComponent(entity.Id, new PositionComponent(X, Y));
        Spawner?.Invoke(entity.Id, ecs);
    }
}
