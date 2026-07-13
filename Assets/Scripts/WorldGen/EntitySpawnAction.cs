using System;

// IWorldGenAction that creates a single entity at (X, Y) and delegates the rest of its
// component setup to a Spawner lambda. Enqueue via WorldGenHandler.EnqueueAction during a
// WorldGenFeature.Generate call; the action runs on the server after all tiles are filled.
//
// SpawnTree is a ready-made Spawner for a neutral respawnable tree resource.
public class EntitySpawnAction : IWorldGenAction
{
    private const int   TreeMaxHealth     = 100;
    private const float TreeBlockRadius   = 1f;
    private const float TreeRespawnSeconds = 30f;
    private const float TreeSelectionScale = 2f;

    public float X;
    public float Y;

    // Called with the new entity's id and the ECS after PositionComponent has been added.
    public Action<ulong, ECS> Spawner;

    // Neutral respawnable tree: active after the first tick, health restores after 30 s.
    public static readonly Action<ulong, ECS> SpawnTree = (id, ecs) =>
    {
        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId    = TroopComponent.NEUTRAL_OWNER_PLAYER_ID,
            _ticksUntilActive = 1,   // activates on the first tick so the renderer fires OnEntityActivated
        });
        ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.Tree });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID, Scale = TreeSelectionScale });
        ecs.AddComponent(id, new StatsComponent { MaxHealth = TreeMaxHealth });
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = TreeMaxHealth });
        ecs.AddComponent(id, new BuildingComponent { BlockRadius = TreeBlockRadius });
        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Wood = 20
        } } );
        ecs.AddComponent(id, new RespawnableInPlaceComponent
        {
            CooldownTicks = (ulong)TickManager.SecondsToTicks(TreeRespawnSeconds),
        });
    };

    public void Execute(ECS ecs)
    {
        EntityHandle entity = ecs.CreateEntity();
        ecs.AddComponent(entity.Id, new PositionComponent(X, Y));
        Spawner?.Invoke(entity.Id, ecs);
    }
}
