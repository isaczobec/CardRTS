using System;
using System.Collections.Generic;

// Shared "spawn a troop" logic for troop-type cards (not buildings — those have no
// MovableComponent/leash and are simple enough to just build inline in BuildingCard).
// Creates the entity and adds every component a troop needs regardless of which kind it
// is (Position, Troop, Renderable, Selectable, Movable, AIMode, Stats, Health), then runs
// each of extraComponents so the calling card can add whatever makes it the specific troop
// it is (an AI component, a projectile pool, ...) without this helper needing to know about
// any of them. AIModeComponent defaults to Guard here — see BasicMeleeAISystem/
// BasicRangedAISystem for what each mode does; buildings never get one, since they go
// through BuildingSpawnHelper instead and have no MovableComponent/AI component either.
public static class TroopCardHelper
{
    private const float DefaultActivationDelaySeconds = 2f;
    private const float SelectionScale = 1f;

    // Default Gold a troop drops to whoever kills it (see OnDeathResourceDropComponent/
    // OnDeathResourceDropSystem) — override via goldDropOnDeath below for a card whose own
    // balance calls for something different. Public so a card whose single play spawns
    // SEVERAL troops at once (SkeletonsCard/EphemeralSkeletonsCard) can divide this evenly
    // across them, so the whole card still only drops this much total if every one of them
    // is killed, rather than that amount N times over.
    public const int DefaultGoldDropOnDeath = 30; // +30% (explicit design ask) from 23

    public static ulong SpawnTroop(
        ECS ecs,
        ushort ownerPlayerId,
        float x, float y,
        RenderableType renderableType,
        StatsComponent stats,
        List<Action<ECS, ulong>> extraComponents,
        float SelectionScale = SelectionScale,
        int goldDropOnDeath = DefaultGoldDropOnDeath)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));

        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId = ownerPlayerId,
            IsPhysicalTroop = true,
        });

        ulong ticksUntilActive = (ulong)TickManager.SecondsToTicks(DefaultActivationDelaySeconds);
        ecs.AddComponent(id, new ActivatableComponent
        {
            _ticksUntilActive       = ticksUntilActive,
            InitialTicksUntilActive = ticksUntilActive,
        });

        ecs.AddComponent(id, new RenderableComponent { Type = renderableType });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = ownerPlayerId, Scale = SelectionScale });

        ecs.AddComponent(id, new MovableComponent
        {
            destinationX = x,
            destinationY = y,
            LeashX        = x,
            LeashY        = y,
        });

        ecs.AddComponent(id, new AIModeComponent { Mode = AIMode.Guard });

        ecs.AddComponent(id, stats);
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = stats.MaxHealth });

        ecs.AddComponent(id, new OnDeathResourceDropComponent { Drop = new ResourceCost
        {
            Gold = goldDropOnDeath
        } } );

        if (extraComponents != null)
            foreach (Action<ECS, ulong> addComponent in extraComponents)
                addComponent(ecs, id);

        return id;
    }
}
