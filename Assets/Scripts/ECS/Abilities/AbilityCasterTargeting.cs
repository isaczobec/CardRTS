using System.Collections.Generic;
using UnityEngine;

// Shared "which of the player's own troops actually cast this ability" resolution for the
// player-wide ability bar (see AbilityBarComponent) — used by both AbilityInputManager (to
// decide who actually casts on a hotkey release) and AbilityIndicatorManager (to decide whose
// range/cast indicators to preview while the hotkey is held), so the two can never disagree
// about who's about to cast. Driven entirely by the per-ability fields on Ability
// (CasterSelectionRadius/MaxSimultaneousCasters/PrioritizeSelectedTroops — see their own doc
// comments) plus AbilityEligibility.CanCast for the underlying "is this troop even allowed to
// cast right now" gate.
public static class AbilityCasterTargeting
{
    // results is caller-owned (reused across calls, e.g. once per frame) rather than
    // static/shared here — same reasoning EntityTargeting.FindClosestSelectable's own
    // scratchBuffer documents — cleared then filled with the resolved casters, closest to
    // (cursorX, cursorY) first.
    public static void FindCasters(
        ECS ecs, int abilityId, Ability ability, float cursorX, float cursorY,
        ushort localPlayerId, IReadOnlyCollection<ulong> selectedEntityIds,
        List<ulong> results)
    {
        results.Clear();
        if (ecs == null || ability == null) return;

        ComponentStore<AbilityComponent> abilityStore = ecs.GetComponentStore<AbilityComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (abilityStore == null || troopStore == null || posStore == null) return;

        // Every owned troop currently allowed to cast this ability at all (ownership,
        // activation/CanPerform, equipped+off-cooldown/has charges — see
        // AbilityEligibility.CanCast), regardless of cursor distance or selection — the full
        // candidate pool both branches below draw from.
        List<(ulong id, float distSq)> candidates = new List<(ulong, float)>();
        abilityStore.ForEach((ulong id) =>
        {
            if (!AbilityEligibility.CanCast(ecs, id, abilityId, localPlayerId, abilityStore, troopStore, out _)) return;
            if (!posStore.HasComponent(id)) return;

            PositionComponent pos = posStore.GetComponent(id);
            float dx = pos.X - cursorX, dy = pos.Y - cursorY;
            candidates.Add((id, dx * dx + dy * dy));
        });

        if (candidates.Count == 0) return;

        if (ability.PrioritizeSelectedTroops && selectedEntityIds != null && selectedEntityIds.Count > 0)
        {
            // "Possesses the ability type" — equipped at all, independent of cooldown; a
            // selected squad that's currently all on cooldown should just do nothing, not
            // silently fall back to casting from unselected troops instead.
            bool anySelectedHasAbility = false;
            foreach (ulong id in selectedEntityIds)
            {
                if (!abilityStore.HasComponent(id)) continue;
                if (abilityStore.GetComponent(id).FindSlot(abilityId) < 0) continue;
                anySelectedHasAbility = true;
                break;
            }

            if (anySelectedHasAbility)
            {
                HashSet<ulong> selectedSet = new HashSet<ulong>(selectedEntityIds);
                candidates.RemoveAll(c => !selectedSet.Contains(c.id));
                WriteCapped(candidates, ability.MaxSimultaneousCasters, results);
                return;
            }
        }

        float radiusSq = ability.CasterSelectionRadius * ability.CasterSelectionRadius;
        candidates.RemoveAll(c => c.distSq > radiusSq);
        WriteCapped(candidates, ability.MaxSimultaneousCasters, results);
    }

    private static void WriteCapped(List<(ulong id, float distSq)> candidates, int maxCasters, List<ulong> results)
    {
        candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));
        int count = maxCasters <= 0 ? candidates.Count : Mathf.Min(maxCasters, candidates.Count);
        for (int i = 0; i < count; i++)
            results.Add(candidates[i].id);
    }
}
