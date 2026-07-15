// Counts every AbilityComponent's 4 cooldown slots down by one tick each (floor at 0).
// Kept separate from AbilitySystem (which sets a slot's cooldown on a successful cast) so
// the countdown itself doesn't have to be duplicated anywhere a cast could happen.
public static class AbilityCooldownSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<AbilityComponent> abilityStore = ecs.GetComponentStore<AbilityComponent>();

        abilityStore.ForEach((ulong id) =>
        {
            ref AbilityComponent abilities = ref abilityStore.GetComponent(id);
            bool changed = false;

            for (int slot = 0; slot < 4; slot++)
            {
                int remaining = abilities.GetCooldownTicksRemaining(slot);
                if (remaining <= 0) continue;
                abilities.SetCooldownTicksRemaining(slot, remaining - 1);
                changed = true;
            }

            if (changed)
                ecs.Delta.MarkComponentDirty(id, typeof(AbilityComponent));
        });
    }
}
