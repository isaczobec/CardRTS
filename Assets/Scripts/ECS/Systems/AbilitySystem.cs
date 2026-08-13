using System.Collections.Generic;
using UnityEngine;

// Reads AbilityUsedInput/AbilityUsedAtLocationInput/AbilityUsedOnEntityInput each tick. For
// each, validates the casting entity exists, is owned by the requesting client, can
// currently take actions, has the requested ability equipped in one of its 4
// AbilityComponent slots, that slot isn't on cooldown, and — for a TargetLocation ability,
// that the point is within the ability's own Range of the caster, or for a TargetEntity
// ability, that the target exists/is selectable, matches CanTargetFriendly/
// CanTargetEnemyOrNeutral, and is within Range — then dispatches to whichever of Ability.
// ExecuteInstant/ExecuteAtLocation/ExecuteOnEntity matches the input actually received, and
// puts that slot on cooldown (AbilityComponent's own per-slot CooldownTicks; see
// AbilityCooldownSystem for the countdown).
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
                ExecuteInstant(ecs, input, abilityStore, troopStore, posStore);

        List<AbilityUsedAtLocationInput> locationInputs = ecs.GetInputsForTick<AbilityUsedAtLocationInput>();
        if (locationInputs != null)
            foreach (AbilityUsedAtLocationInput input in locationInputs)
                ExecuteAtLocation(ecs, input, abilityStore, troopStore, posStore);

        ComponentStore<SelectableComponent> selectableStore = ecs.GetComponentStore<SelectableComponent>();
        List<AbilityUsedOnEntityInput> entityInputs = ecs.GetInputsForTick<AbilityUsedOnEntityInput>();
        if (entityInputs != null)
            foreach (AbilityUsedOnEntityInput input in entityInputs)
                ExecuteOnEntity(ecs, input, abilityStore, troopStore, posStore, selectableStore);
    }

    private static void ExecuteInstant(ECS ecs, AbilityUsedInput input,
        ComponentStore<AbilityComponent> abilityStore, ComponentStore<TroopComponent> troopStore, ComponentStore<PositionComponent> posStore)
    {
        if (!TryBeginCast(ecs, input.CastingEntityId, input.AbilityId, input.ClientId, abilityStore, troopStore, out Ability ability, out int slot))
            return;

        if (ability.ExecuteInstant == null)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: ability {input.AbilityId} has no instant-activate behavior.", "abilities");
            return;
        }

        ability.ExecuteInstant(ecs, input);

        // No click point exists for an Instant ability — falls back to the caster's own
        // position (see AbilityPerformedEvent's own doc comment).
        float worldX = 0f, worldY = 0f;
        if (posStore != null && posStore.HasComponent(input.CastingEntityId))
        {
            PositionComponent casterPos = posStore.GetComponent(input.CastingEntityId);
            worldX = casterPos.X;
            worldY = casterPos.Y;
        }

        CommitCooldown(ecs, input.CastingEntityId, slot, abilityStore, worldX, worldY);
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
        CommitCooldown(ecs, input.CastingEntityId, slot, abilityStore, input.X, input.Y);
    }

    private static void ExecuteOnEntity(ECS ecs, AbilityUsedOnEntityInput input,
        ComponentStore<AbilityComponent> abilityStore, ComponentStore<TroopComponent> troopStore,
        ComponentStore<PositionComponent> posStore, ComponentStore<SelectableComponent> selectableStore)
    {
        if (!TryBeginCast(ecs, input.CastingEntityId, input.AbilityId, input.ClientId, abilityStore, troopStore, out Ability ability, out int slot))
            return;

        if (ability.ExecuteOnEntity == null)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: ability {input.AbilityId} has no target-entity behavior.", "abilities");
            return;
        }

        if (!ecs.HasEntity(input.TargetEntityId) || selectableStore == null || !selectableStore.HasComponent(input.TargetEntityId))
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: target entity {input.TargetEntityId} does not exist or is not selectable.", "abilities");
            return;
        }

        // Never trust the client's own friend/enemy filtering (EntityTargeting on the
        // input-capture side) — re-derive it here from authoritative state.
        bool isFriendly = selectableStore.GetComponent(input.TargetEntityId).OwnerPlayerId == input.ClientId;
        if (isFriendly && !ability.CanTargetFriendly || !isFriendly && !ability.CanTargetEnemyOrNeutral)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: ability {input.AbilityId} cannot target {(isFriendly ? "friendly" : "enemy/neutral")} entity {input.TargetEntityId}.", "abilities");
            return;
        }

        bool isSelectable = ecs.Requests.Process(new IsSelectableRequest(input.TargetEntityId, input.ClientId), ecs, executeIfNotCancelled: false).IsSelectable;
        if (!isSelectable)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: target entity {input.TargetEntityId} is not currently selectable.", "abilities");
            return;
        }

        if (posStore == null || !posStore.HasComponent(input.CastingEntityId) || !posStore.HasComponent(input.TargetEntityId))
            return;

        PositionComponent casterPos = posStore.GetComponent(input.CastingEntityId);
        PositionComponent targetPos = posStore.GetComponent(input.TargetEntityId);
        float dx = targetPos.X - casterPos.X, dy = targetPos.Y - casterPos.Y;
        if (dx * dx + dy * dy > ability.Range * ability.Range + RangeToleranceSq)
        {
            DebugLogger.LogWarning($"[AbilitySystem] Rejected: target entity {input.TargetEntityId} is outside ability {input.AbilityId}'s range ({ability.Range}) of entity {input.CastingEntityId}.", "abilities");
            return;
        }

        ability.ExecuteOnEntity(ecs, input);
        CommitCooldown(ecs, input.CastingEntityId, slot, abilityStore, targetPos.X, targetPos.Y);
    }

    // Shared validation for both input kinds. Out params are only meaningful when this
    // returns true. The eligibility gate itself (ownership/activation/cooldown/charges) lives
    // in AbilityEligibility.CanCast, shared with AbilityCasterTargeting's client-side caster
    // selection so the two can never disagree about who's allowed to cast.
    private static bool TryBeginCast(ECS ecs, ulong casterId, int abilityId, ushort clientId,
        ComponentStore<AbilityComponent> abilityStore, ComponentStore<TroopComponent> troopStore,
        out Ability ability, out int slot)
    {
        ability = null;

        // An ability order is always allowed to land, even mid-recall — it just ends the
        // recall immediately instead of being silently blocked by CanPerformRequest's own
        // veto (see RecallSystem.CancelRecall). Cancelled before CanCast runs below so a
        // legitimate cast can still succeed the same tick.
        RecallSystem.CancelRecall(ecs, casterId);

        if (!AbilityEligibility.CanCast(ecs, casterId, abilityId, clientId, abilityStore, troopStore, out slot))
            return false;

        return AbilityManager.TryGet(abilityId, out ability);
    }

    private static void CommitCooldown(ECS ecs, ulong casterId, int slot, ComponentStore<AbilityComponent> abilityStore, float worldX, float worldY)
    {
        ref AbilityComponent abilities = ref abilityStore.GetComponent(casterId);
        abilities.SetCooldownTicksRemaining(slot, abilities.GetCooldownTicks(slot));

        int maxCharges = abilities.GetMaxCharges(slot);
        if (maxCharges > 1)
        {
            int chargesRemaining = abilities.GetChargesRemaining(slot);
            bool wasFull = chargesRemaining >= maxCharges;
            abilities.SetChargesRemaining(slot, Mathf.Max(0, chargesRemaining - 1));

            // Only one charge regenerates at a time — the timer only (re)starts once a
            // charge is spent from a full stack; if one was already recharging, it just
            // keeps counting down uninterrupted (see AbilityChargeSystem).
            if (wasFull)
                abilities.SetChargeCooldownTicksRemaining(slot, abilities.GetChargeCooldownTicks(slot));
        }

        ecs.Delta.MarkComponentDirty(casterId, typeof(AbilityComponent));
        ecs.FlagEvents.Add(new AbilityPerformedEvent { EntityId = casterId, Slot = slot, WorldX = worldX, WorldY = worldY });
    }
}
