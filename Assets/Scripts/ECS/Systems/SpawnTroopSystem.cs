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

    // Scaled to match BasicMeleeTroopCard's rebalance baseline (3x health, 2.8x damage,
    // then damage 1.2x again to compensate for 20 armor under ArmorMitigationSystem).
    private const int DefaultMaxHealth = 300;
    private const int DefaultSpeed = 10;
    private const int DefaultArmor = 20;
    private const int DefaultDamage = 34;
    private const int DefaultSpellResist = 0;
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

    private const int BuildingDefaultMaxHealth = 900;
    private const int BuildingDefaultArmor = 40;
    private const int BuildingDefaultSpellResist = 150;
    private const float BuildingDefaultBlockRadius = 3f;

    private const float TroopSelectionScale = 1f;
    private const float BuildingSelectionScale = 3f;

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
        if (input.TroopType == TroopType.Building)
        {
            SpawnBuilding(ecs, input);
            return;
        }

        bool isRanged = input.TroopType == TroopType.BasicRanged;

        EntityHandle entity = ecs.CreateEntity();

        ecs.AddComponent(entity.Id, new PositionComponent(input.X, input.Y));

        ecs.AddComponent(entity.Id, new TroopComponent
        {
            OwnerPlayerId = input.ClientId,
            IsPhysicalTroop = true,
        });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(DefaultActivationDelaySeconds);
        ecs.AddComponent(entity.Id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        DebugLogger.Log($"Spawned {input.TroopType} troop entity {entity.Id} for player {input.ClientId} at ({input.X}, {input.Y})");

        ecs.AddComponent(entity.Id, new RenderableComponent
        {
            Type = isRanged ? RenderableType.BasicRanged : RenderableType.BasicMelee,
        });

        ecs.AddComponent(entity.Id, new SelectableComponent
        {
            OwnerPlayerId = input.ClientId,
            Scale         = TroopSelectionScale,
        });

        ecs.AddComponent(entity.Id, new MovableComponent
        {
            destinationX = input.X,
            destinationY = input.Y,
            LeashX = input.X,
            LeashY = input.Y,
        });

        ecs.AddComponent(entity.Id, new StatsComponent
        {
            MaxHealth   = DefaultMaxHealth,
            Speed       = DefaultSpeed,
            Range       = isRanged ? RangedDefaultRange : MeleeDefaultRange,
            Armor       = DefaultArmor,
            Damage      = DefaultDamage,
            AttackSpeed = TickManager.MillisecondsToTicks(isRanged ? RangedDefaultAttackSpeedMilliseconds : MeleeDefaultAttackSpeedMilliseconds),
            SpellResist = DefaultSpellResist,
        });

        ecs.AddComponent(entity.Id, new HealthComponent
        {
            CurrentHealth = DefaultMaxHealth,
        });

        if (isRanged)
        {
            ecs.AddComponent(entity.Id, new BasicRangedAIComponent
            {
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
                DetectionRangeMultiplier   = DefaultDetectionRangeMultiplier,
                ChaseRangeMultiplier       = DefaultChaseRangeMultiplier,
                AttackRangeMultiplier      = DefaultAttackRangeMultiplier,
            });
        }
    }

    // Buildings are stationary, non-AI entities: no MovableComponent (they never move)
    // and no AI component (they don't act) — just enough to be selectable, damageable,
    // and visible, plus BuildingComponent so BuildingBlockingSystem keeps idle troops
    // from standing inside it.
    private static void SpawnBuilding(ECS ecs, SpawnTroopInput input)
    {
        EntityHandle entity = ecs.CreateEntity();

        ecs.AddComponent(entity.Id, new PositionComponent(input.X, input.Y));

        ecs.AddComponent(entity.Id, new TroopComponent
        {
            OwnerPlayerId = input.ClientId,
            IsPhysicalTroop = false,
        });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(DefaultActivationDelaySeconds);
        ecs.AddComponent(entity.Id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        DebugLogger.Log($"Spawned Building troop entity {entity.Id} for player {input.ClientId} at ({input.X}, {input.Y})");

        ecs.AddComponent(entity.Id, new RenderableComponent
        {
            Type = RenderableType.BasicBuilding,
        });

        ecs.AddComponent(entity.Id, new SelectableComponent
        {
            OwnerPlayerId = input.ClientId,
            Scale         = BuildingSelectionScale,
        });

        ecs.AddComponent(entity.Id, new StatsComponent
        {
            MaxHealth   = BuildingDefaultMaxHealth,
            Armor       = BuildingDefaultArmor,
            SpellResist = BuildingDefaultSpellResist,
            // Speed/Range/Damage/AttackSpeed left at 0 — buildings don't move or attack.
        });

        ecs.AddComponent(entity.Id, new HealthComponent
        {
            CurrentHealth = BuildingDefaultMaxHealth,
        });

        ecs.AddComponent(entity.Id, new BuildingComponent
        {
            BlockRadius             = BuildingDefaultBlockRadius,
            CardPlayRangeMultiplier = 1f,
        });
    }

    // See ProjectilePool.CreatePool for the ring shape, and ProjectilePool.Fire for how
    // it's walked when firing.
    private static void SpawnProjectilePool(ECS ecs, ulong ownerId, int count)
    {
        ulong firstId = ProjectilePool.CreatePool(ecs, ownerId, count, RangedProjectileSpeedMilliTilesPerSecond);
        ecs.AddComponent(ownerId, new ProjectileOwnerComponent
        {
            MaxProjectiles   = count,
            NextProjectileId = firstId,
        });
    }
}
