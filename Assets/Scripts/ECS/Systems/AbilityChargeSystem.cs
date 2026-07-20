// Regenerates each AbilityComponent slot's charges over time, for any slot with
// MaxCharges > 1 (a MaxCharges <= 1 slot never touches charges at all — see
// AbilityComponent's own doc comment). Only one charge regenerates at a time: the
// countdown (re)starts the instant ChargesRemaining drops below MaxCharges (normally by
// AbilitySystem.CommitCooldown right after a cast, but also caught here as a fallback for
// a slot somehow granted already below max with no timer running) and, once it finishes,
// restarts immediately for the next charge until back at MaxCharges. Kept separate from
// AbilityCooldownSystem (which counts down the ordinary per-cast cooldown) since the two
// timers are independent and unrelated.
public static class AbilityChargeSystem
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
                int maxCharges = abilities.GetMaxCharges(slot);
                if (maxCharges <= 1) continue;

                int chargesRemaining = abilities.GetChargesRemaining(slot);
                if (chargesRemaining >= maxCharges) continue;

                int ticksRemaining = abilities.GetChargeCooldownTicksRemaining(slot);
                if (ticksRemaining <= 0)
                {
                    // Not currently counting down — (re)start the timer for the next
                    // charge. No ChargeCooldownTicks configured means there's nothing
                    // sensible to count down, so leave it be.
                    ticksRemaining = abilities.GetChargeCooldownTicks(slot);
                    if (ticksRemaining <= 0) continue;
                }

                ticksRemaining--;
                if (ticksRemaining <= 0)
                {
                    chargesRemaining++;
                    abilities.SetChargesRemaining(slot, chargesRemaining);
                    ticksRemaining = chargesRemaining < maxCharges ? abilities.GetChargeCooldownTicks(slot) : 0;
                }

                abilities.SetChargeCooldownTicksRemaining(slot, ticksRemaining);
                changed = true;
            }

            if (changed)
                ecs.Delta.MarkComponentDirty(id, typeof(AbilityComponent));
        });
    }
}
