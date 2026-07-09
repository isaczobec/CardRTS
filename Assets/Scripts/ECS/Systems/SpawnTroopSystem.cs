using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Server-only. Reads SpawnTroopInput each tick and creates a troop entity whose
/// tickToBecomeActive is 20 ticks ahead, giving the delta time to reach the client
/// before the troop activates.
/// </summary>
public static class SpawnTroopSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const int DefaultMaxHealth = 100;
    private const int DefaultSpeed = 10;
    private const int DefaultRange = 5;
    private const int DefaultArmor = 0;
    private const int DefaultDamage = 10;
    private const int DefaultAttackSpeed = 10;

    private const float DefaultDetectionRangeMultiplier = 3f;
    private const float DefaultChaseRangeMultiplier = 5f;
    private const float DefaultAttackRangeMultiplier = 1.5f;

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<SpawnTroopInput> inputs = ecs.GetInputsForTick<SpawnTroopInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (var input in inputs)
        {
            EntityHandle entity = ecs.CreateEntity();

            ecs.AddComponent(entity.Id, new PositionComponent(input.X, input.Y));

            ecs.AddComponent(entity.Id, new TroopComponent
            {
                OwnerPlayerId      = input.ClientId,
                _ticksUntilActive = 20,
            });

            DebugLogger.Log($"Spawned troop entity {entity.Id} for player {input.ClientId} at ({input.X}, {input.Y})");
            ecs.AddComponent(entity.Id, new RenderableComponent
            {
                Type = RenderableType.BasicMelee,
            });

            ecs.AddComponent(entity.Id, new SelectableComponent
            {
                OwnerPlayerId = input.ClientId,
            });

            ecs.AddComponent(entity.Id, new MovableComponent
            {
                destinationX = input.X,
                destinationY = input.Y,
            });

            ecs.AddComponent(entity.Id, new StatsComponent
            {
                MaxHealth   = DefaultMaxHealth,
                Speed       = DefaultSpeed,
                Range       = DefaultRange,
                Armor       = DefaultArmor,
                Damage      = DefaultDamage,
                AttackSpeed = DefaultAttackSpeed,
            });

            ecs.AddComponent(entity.Id, new HealthComponent
            {
                CurrentHealth = DefaultMaxHealth,
            });

            ecs.AddComponent(entity.Id, new BasicMeleeAIComponent
            {
                OriginalX                 = input.X,
                OriginalY                 = input.Y,
                DetectionRangeMultiplier   = DefaultDetectionRangeMultiplier,
                ChaseRangeMultiplier       = DefaultChaseRangeMultiplier,
                AttackRangeMultiplier      = DefaultAttackRangeMultiplier,
            });
        }
    }
}
