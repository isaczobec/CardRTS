// Resets ShadowAngelComponent.HasRedistributedDamageThisTick back to false at the start of
// every tick — see ShadowAngelComponent's own doc comment for why that flag exists and why
// it needs resetting exactly once per tick. Registered early in TickManager's system list —
// well before ShadowAngelDamageShareSystem's DamageRequest subscriber can possibly fire this
// tick (that only happens once DamageResolutionSystem's Flush runs, much later in the same
// tick) — rather than folded into ShadowAngelDamageShareSystem's own Execute, since that
// system needs to stay registered LATE (see TickManager's own comment on it) purely so its
// Setup call installs its Subscribe<DamageRequest> callback last among every other
// DamageRequest subscriber; moving its registration earlier for this reset would also move
// that Subscribe call earlier, breaking the ordering it actually depends on.
public static class ShadowAngelTickResetSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<ShadowAngelComponent> store = ecs.GetComponentStore<ShadowAngelComponent>();
        if (store == null) return;

        store.ForEach((ulong id) =>
        {
            ref ShadowAngelComponent angel = ref store.GetComponent(id);
            if (!angel.HasRedistributedDamageThisTick) return;

            angel.HasRedistributedDamageThisTick = false;
            ecs.Delta.MarkComponentDirty(id, typeof(ShadowAngelComponent));
        });
    }
}
