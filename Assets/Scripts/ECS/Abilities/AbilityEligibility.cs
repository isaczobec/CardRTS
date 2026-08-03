// Shared "is casterId currently allowed to cast abilityId" gate — used by both
// AbilitySystem.TryBeginCast (server-authoritative, the only place that actually matters) and
// AbilityCasterTargeting (client-side caster selection/preview for the ability bar), so the
// two can never disagree about which of the player's troops are eligible right now. Checks
// ownership, whether the caster can currently take actions at all, whether it has abilityId
// equipped in one of its 4 slots, and whether that slot is off-cooldown (with charges
// remaining, for a MaxCharges > 1 slot) — everything AbilitySystem itself would check before
// actually running the ability, except the ability-specific Range/target checks, which differ
// per AbilityType and are resolved separately (see AbilityTargeting/EntityTargeting).
public static class AbilityEligibility
{
    public static bool CanCast(ECS ecs, ulong casterId, int abilityId, ushort ownerPlayerId,
        ComponentStore<AbilityComponent> abilityStore, ComponentStore<TroopComponent> troopStore,
        out int slot)
    {
        slot = -1;

        if (abilityStore == null || troopStore == null) return false;
        if (!abilityStore.HasComponent(casterId) || !troopStore.HasComponent(casterId)) return false;
        if (troopStore.GetComponent(casterId).OwnerPlayerId != ownerPlayerId) return false;
        if (!ActivationQuery.IsActivated(ecs, casterId)) return false;
        // Casting an ability is itself an "action" a silence (or similar) can veto,
        // independently of whether the caster is activated/alive at all.
        if (!ActivationQuery.CanPerform(ecs, casterId)) return false;

        AbilityComponent abilities = abilityStore.GetComponent(casterId);
        slot = abilities.FindSlot(abilityId);
        if (slot < 0) return false;
        if (abilities.GetCooldownTicksRemaining(slot) > 0) return false;
        // A MaxCharges <= 1 slot never consults charges at all — CooldownTicksRemaining
        // above is the only gate, exactly as before charges existed.
        if (abilities.GetMaxCharges(slot) > 1 && abilities.GetChargesRemaining(slot) <= 0) return false;

        return true;
    }
}
