using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Server-only. Reads SpawnTroopInput each tick and creates a troop entity (per
/// input.TroopType) whose tickToBecomeActive is DefaultActivationDelaySeconds ahead,
/// giving the delta time to reach the client before the troop activates. A ranged troop
/// additionally gets a pre-allocated ring of pooled projectile entities — see
/// ProjectileOwnerComponent/ProjectileBaseComponent and ProjectilePool.
/// </summary>
public static class SpawnTroopSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const int DefaultMaxHealth = 100;
    private const int DefaultSpeed = 10;
    private const int DefaultArmor = 0;
    private const int DefaultDamage = 10;
    private const float DefaultActivationDelaySeconds = 2f;

    private const float DefaultDetectionRangeMultiplier = 3f;
    private const float DefaultChaseRangeMultiplier = 5f;
    private const float DefaultAttackRangeMultiplier = 1.5f;

    private const int MeleeDefaultRange = 5;
    private const float MeleeDefaultAttackSpeedMilliseconds = 333f;

    private const int RangedDefaultRange = 60;
    private const float RangedDefaultAttackSpeedMilliseconds = 800f;
    private const int RangedProjectilePoolSize = 64;
    private const int RangedProjectileSpeedMilliTilesPerSecond = 15000; // 15 tiles/sec

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<SpawnTroopInput> inputs = ecs.GetInputsForTick<SpawnTroopInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (var input in inputs)
            SpawnTroop(ecs, input);
    }

    private static void SpawnTroop(ECS ecs, SpawnTroopInput input)
    {
        bool isRanged = input.TroopType == TroopType.BasicRanged;

        EntityHandle entity = ecs.CreateEntity();

        ecs.AddComponent(entity.Id, new PositionComponent(input.X, input.Y));

        ecs.AddComponent(entity.Id, new TroopComponent
        {
            OwnerPlayerId      = input.ClientId,
            _ticksUntilActive = (ulong)TickManager.SecondsToTicks(DefaultActivationDelaySeconds),
        });

        DebugLogger.Log($"Spawned {input.TroopType} troop entity {entity.Id} for player {input.ClientId} at ({input.X}, {input.Y})");

        ecs.AddComponent(entity.Id, new RenderableComponent
        {
            Type = isRanged ? RenderableType.BasicRanged : RenderableType.BasicMelee,
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
            Range       = isRanged ? RangedDefaultRange : MeleeDefaultRange,
            Armor       = DefaultArmor,
            Damage      = DefaultDamage,
            AttackSpeed = TickManager.MillisecondsToTicks(isRanged ? RangedDefaultAttackSpeedMilliseconds : MeleeDefaultAttackSpeedMilliseconds),
        });

        ecs.AddComponent(entity.Id, new HealthComponent
        {
            CurrentHealth = DefaultMaxHealth,
        });

        if (isRanged)
        {
            ecs.AddComponent(entity.Id, new BasicRangedAIComponent
            {
                OriginalX                 = input.X,
                OriginalY                 = input.Y,
                DetectionRangeMultiplier   = DefaultDetectionRangeMultiplier,
                ChaseRangeMultiplier       = DefaultChaseRangeMultiplier,
                AttackRangeMultiplier      = DefaultAttackRangeMultiplier,
            });

            SpawnProjectilePool(ecs, entity.Id, RangedProjectilePoolSize);
        }
        else
        {
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

    // Pre-allocates a ring of `count` pooled projectile entities for a ranged troop, each
    // linked to the next (last wraps back to the first) via ProjectileBaseComponent.
    // NextProjectileId, and points the owner's ProjectileOwnerComponent.NextProjectileId
    // at the first one. See ProjectilePool.Fire for how the ring is walked when firing.
    private static void SpawnProjectilePool(ECS ecs, ulong ownerId, int count)
    {
        ComponentStore<ProjectileBaseComponent> projectileStore = ecs.GetComponentStore<ProjectileBaseComponent>();

        ulong firstId = 0;
        ulong previousId = 0;

        for (int i = 0; i < count; i++)
        {
            EntityHandle projectile = ecs.CreateEntity();
            ulong id = projectile.Id;
            if (i == 0) firstId = id;

            ecs.AddComponent(id, new PositionComponent(0f, 0f));
            ecs.AddComponent(id, new RenderableComponent { Type = RenderableType.SeekingProjectile });
            ecs.AddComponent(id, new SeekingProjectileComponent { Speed = RangedProjectileSpeedMilliTilesPerSecond });
            ecs.AddComponent(id, new ProjectileBaseComponent { OwnerEntityId = ownerId, IsActive = false });

            if (previousId != 0)
            {
                ref ProjectileBaseComponent previous = ref projectileStore.GetComponent(previousId);
                previous.NextProjectileId = id;
            }

            previousId = id;
        }

        ref ProjectileBaseComponent last = ref projectileStore.GetComponent(previousId);
        last.NextProjectileId = firstId;

        ecs.AddComponent(ownerId, new ProjectileOwnerComponent
        {
            MaxProjectiles   = count,
            NextProjectileId = firstId,
        });
    }
}
