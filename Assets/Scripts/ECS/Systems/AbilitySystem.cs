using System.Collections.Generic;

// Reads AbilityUsedInput/AbilityUsedAtLocationInput each tick. For each, validates the
// casting entity exists, is owned by the requesting client, can currently take actions,
// has the requested ability equipped in one of its 4 AbilityComponent slots, that slot
// isn't on cooldown, and (for a TargetLocation ability) that the point is within the
// ability's own Range of the caster — then dispatches to whichever of Ability.
// ExecuteInstant/ExecuteAtLocation matches the input actually received, and puts that slot
// on cooldown (AbilityComponent's own per-slot CooldownTicks; see AbilityCooldownSystem
// for the countdown).
//
// Stateless, so registered as a GlobalSystem the same way SpawnAtPointCardPlaySystem is.
// Runs unconditionally (predicted on clients) — abilities that only create/mutate
// components (like this system's two test abilities) are safe to predict; one that needs
// to be server-only would have to guard itself the way SpawnAtPointCardPlaySystem does.
public static class AbilitySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    // Slack added to the range check below, in squared world units (~0.1 units of linear
    // slack). AbilityTargeting.ResolveCastPoint clamps a client's cast point to exactly
    // Range away via sqrt/divide/multiply, which is only exact up to float precision — a
    // legitimately-clamped point can land a hair over Range*Range once this recomputes it,
    // and a strict rejection there would bounce every cast made right at the edge of the
    // range circle (exactly where a player clamping their aim is likely to click). This
    // also absorbs any small drift between a client's predicted caster position at clamp
    // time and the server's authoritative one by the tick this actually runs.
    private const float RangeToleranceSq = 0.1f;

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<AbilityComponent> abilityStore = ecs.GetComponentStore<AbilityComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        List<AbilityUsedInput> instantInputs = ecs.GetInputsForTick<AbilityUsedInput>();
        if (instantInputs != null)
            foreach (AbilityUsedInput input in instantInputs)
                ExecuteInstant(ecs, input, abilityStore, troopStore);

        List<AbilityUsedAtLocationInput> locationInputs = ecs.GetInputsForTick<AbilityUsedAtLocationInput>();
        if (locationInputs != null)
            foreach (AbilityUsedAtLocationInput input in locationInputs)
                ExecuteAtLocation(ecs, input, abilityStore, troopStore, posStore);
    }

    private static void ExecuteInstant(ECS ecs, AbilityUsedInput input,
        ComponentStore<AbilityComponent> abilityStore, ComponentStore<TroopComponent> troopStore)
    {
        if (!TryBeginCast(ecs, input.CastingEntityId, input.AbilityId, input.ClientId, abilityStore, troopStore, out Ability ability, out int slot))
            return;

        if (ability.ExecuteInstant == null)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: ability {input.AbilityId} has no instant-activate behavior.", "abilities");
            return;
        }

        ability.ExecuteInstant(ecs, input);
        CommitCooldown(ecs, input.CastingEntityId, slot, abilityStore);
    }

    private static void ExecuteAtLocation(ECS ecs, AbilityUsedAtLocationInput input,
        ComponentStore<AbilityComponent> abilityStore, ComponentStore<TroopComponent> troopStore, ComponentStore<PositionComponent> posStore)
    {
        if (!TryBeginCast(ecs, input.CastingEntityId, input.AbilityId, input.ClientId, abilityStore, troopStore, out Ability ability, out int slot))
            return;

        if (ability.ExecuteAtLocation == null)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: ability {input.AbilityId} has no target-location behavior.", "abilities");
            return;
        }

        if (posStore == null || !posStore.HasComponent(input.CastingEntityId))
            return;

        PositionComponent casterPos = posStore.GetComponent(input.CastingEntityId);
        float dx = input.X - casterPos.X, dy = input.Y - casterPos.Y;
        if (dx * dx + dy * dy > ability.Range * ability.Range + RangeToleranceSq)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: target ({input.X}, {input.Y}) is outside ability {input.AbilityId}'s range ({ability.Range}) of entity {input.CastingEntityId}.", "abilities");
            return;
        }

        ability.ExecuteAtLocation(ecs, input);
        CommitCooldown(ecs, input.CastingEntityId, slot, abilityStore);
    }

    // Shared validation for both input kinds. Out params are only meaningful when this
    // returns true.
    private static bool TryBeginCast(ECS ecs, ulong casterId, int abilityId, ushort clientId,
        ComponentStore<AbilityComponent> abilityStore, ComponentStore<TroopComponent> troopStore,
        out Ability ability, out int slot)
    {
        ability = null;
        slot = -1;

        if (abilityStore == null || troopStore == null) return false;
        if (!abilityStore.HasComponent(casterId) || !troopStore.HasComponent(casterId)) return false;
        if (troopStore.GetComponent(casterId).OwnerPlayerId != clientId) return false;
        if (!ActivationQuery.CanTakeActions(ecs, casterId)) return false;

        AbilityComponent abilities = abilityStore.GetComponent(casterId);
        slot = FindSlot(abilities, abilityId);
        if (slot < 0) return false;
        if (abilities.GetCooldownTicksRemaining(slot) > 0) return false;

        return AbilityManager.TryGet(abilityId, out ability);
    }

    private static int FindSlot(AbilityComponent abilities, int abilityId)
    {
        for (int slot = 0; slot < 4; slot++)
            if (abilities.GetAbilityId(slot) == abilityId)
                return slot;
        return -1;
    }

    private static void CommitCooldown(ECS ecs, ulong casterId, int slot, ComponentStore<AbilityComponent> abilityStore)
    {
        ref AbilityComponent abilities = ref abilityStore.GetComponent(casterId);
        abilities.SetCooldownTicksRemaining(slot, abilities.GetCooldownTicks(slot));
        ecs.Delta.MarkComponentDirty(casterId, typeof(AbilityComponent));
    }
}
