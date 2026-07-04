using System.Collections.Generic;
using UnityEngine;

public static class PlayerMovementSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const float Speed = 5f;

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<MoveInput> inputs = ecs.GetInputsForTick<MoveInput>();
        if (inputs == null || inputs.Count == 0) return;

        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        playerStore.ForEach((ulong entityId) =>
        {
            if (!posStore.HasComponent(entityId)) return;

            ref PlayerComponent player = ref playerStore.GetComponent(entityId);

            foreach (MoveInput input in inputs)
            {
                if (input.ClientId != player.PlayerId) continue;

                ref PositionComponent pos = ref posStore.GetComponent(entityId);
                pos.X += input.DirX * Speed * TickManager.TickInterval;
                pos.Y += input.DirY * Speed * TickManager.TickInterval;
                ecs.Delta.MarkComponentDirty(entityId, typeof(PositionComponent));
                flagEvents.Add(new PositionUpdatedEvent());
                break;
            }
        });
    }
}
