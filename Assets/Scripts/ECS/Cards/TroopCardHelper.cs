using System;
using System.Collections.Generic;

// Shared "spawn a troop" logic for troop-type cards (not buildings — those have no
// MovableComponent/leash and are simple enough to just build inline in BuildingCard).
// Creates the entity and adds every component a troop needs regardless of which kind it
// is (Position, Troop, Renderable, Selectable, Movable, Stats, Health), then runs each of
// extraComponents so the calling card can add whatever makes it the specific troop it is
// (an AI component, a projectile pool, ...) without this helper needing to know about any
// of them.
public static class TroopCardHelper
{
    private const float DefaultActivationDelaySeconds = 2f;

    public static ulong SpawnTroop(
        ECS ecs,
        ushort ownerPlayerId,
        float x, float y,
        RenderableType renderableType,
        StatsComponent stats,
        List<Action<ECS, ulong>> extraComponents)
    {
        EntityHandle entity = ecs.CreateEntity();
        ulong id = entity.Id;

        ecs.AddComponent(id, new PositionComponent(x, y));

        ecs.AddComponent(id, new TroopComponent
        {
            OwnerPlayerId     = ownerPlayerId,
            _ticksUntilActive = (ulong)TickManager.SecondsToTicks(DefaultActivationDelaySeconds),
        });

        ecs.AddComponent(id, new RenderableComponent { Type = renderableType });
        ecs.AddComponent(id, new SelectableComponent { OwnerPlayerId = ownerPlayerId });

        ecs.AddComponent(id, new MovableComponent
        {
            destinationX = x,
            destinationY = y,
            LeashX        = x,
            LeashY        = y,
        });

        ecs.AddComponent(id, stats);
        ecs.AddComponent(id, new HealthComponent { CurrentHealth = stats.MaxHealth });

        if (extraComponents != null)
            foreach (Action<ECS, ulong> addComponent in extraComponents)
                addComponent(ecs, id);

        return id;
    }
}
