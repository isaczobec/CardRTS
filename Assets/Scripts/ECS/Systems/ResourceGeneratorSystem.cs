using System;

// Every tick, for each ResourceGeneratorComponent-carrying entity that's actually active
// (deployed — see ActivationQuery.IsActivated), counts TicksUntilNextProc down and, once it
// reaches 0, grants AmountPerProc of Type to the entity's own OWNER player (via
// ResourcesAdded, tagged with this entity's own world position so FloatingTextManager shows
// a "+N" popup right where the building stands — unlike ResourceGenerationSystem's own
// per-player *PerSecond generation, which has no single source position to attach one to)
// and resets the countdown to PeriodTicks. Mirrors DamageOverTimeSystem's own proc-cadence
// shape, but lives directly on the generating entity itself rather than a separate modifier
// entity (see ResourceGeneratorComponent's own doc comment for why).
//
// Stateless, so registered as a single shared GlobalSystem, same as DamageOverTimeSystem.
// Must run BEFORE ResourceGenerationSystem in TickManager, which is what actually flushes
// every pending ResourcesAdded request this tick (see that system's own doc comment) — this
// only ever enqueues one, never flushes.
public static class ResourceGeneratorSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<ResourceGeneratorComponent> genStore = ecs.GetComponentStore<ResourceGeneratorComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (genStore == null || troopStore == null) return;

        genStore.ForEach((ulong id) =>
        {
            if (!troopStore.HasComponent(id)) return;
            if (!ActivationQuery.IsActivated(ecs, id)) return; // don't generate while still deploying

            ref ResourceGeneratorComponent gen = ref genStore.GetComponent(id);
            if (gen.PeriodTicks <= 0) return;

            gen.TicksUntilNextProc--;
            if (gen.TicksUntilNextProc > 0)
            {
                ecs.Delta.MarkComponentDirty(id, typeof(ResourceGeneratorComponent));
                return;
            }

            gen.TicksUntilNextProc = Math.Max(1, gen.PeriodTicks);
            ecs.Delta.MarkComponentDirty(id, typeof(ResourceGeneratorComponent));

            ushort ownerPlayerId = troopStore.GetComponent(id).OwnerPlayerId;
            ulong playerEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, ownerPlayerId);
            if (playerEntityId == 0) return;

            ResourcesAdded request = new ResourcesAdded(playerEntityId, gen.Type, gen.AmountPerProc) { IsPassive = true };
            if (posStore != null && posStore.HasComponent(id))
            {
                PositionComponent pos = posStore.GetComponent(id);
                request.X = pos.X;
                request.Y = pos.Y;
            }

            // Enqueued (not flushed here) — ResourceGenerationSystem, registered later,
            // flushes every pending ResourcesAdded request this same tick.
            ecs.Requests.CreateRequest(request);
        });
    }
}
