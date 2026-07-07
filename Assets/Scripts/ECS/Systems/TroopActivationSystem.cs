// Counts each inactive troop's remaining activation delay down by one tick, and fires
// TroopActivatedEvent the tick it crosses zero. Renderer/selection visuals for a troop
// are gated on that event rather than on TroopComponent simply existing.
public static class TroopActivationSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();

        troopStore.ForEach((ulong id) => {
            ref TroopComponent troop = ref troopStore.GetComponent(id);
            if (troop._ticksUntilActive == 0) return;

            troop._ticksUntilActive--;
            ecs.Delta.MarkComponentDirty(id, typeof(TroopComponent));

            if (troop._ticksUntilActive == 0)
                flagEvents.Add(new TroopActivatedEvent { EntityId = id });
        });
    }
}
