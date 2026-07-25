using UnityEngine;

// Absorbs incoming damage for any entity currently targeted by an active BarrierComponent
// modifier (mirrors PeriodicDamageReductionSystem's own "walk every ModifierComponent
// targeting this entity" shape). Subscribed to DamageRequest LATE in the chain — registered
// after BuildingDamageBonusSystem/ArmorMitigationSystem/PeriodicDamageReductionSystem (see
// TickManager's registration order, which is also Subscribe callback registration order —
// see RequestManager's own doc comment) so it acts on the final, fully-mitigated Amount, and
// before DamageResolutionSystem actually flushes the request against HealthComponent.
//
// Sets Amount to 0 rather than calling request.Cancel() — the hit is still fully "real" in
// every other sense (DamageDealtEvent still fires with Amount 0, HitboxImmunitySystem/combat
// timers etc. still see the request go through), it just deals no HP damage, since a barrier
// blocks the hit rather than making it never happen.
//
// Registered as a GlobalSystem purely for the Setup hook, same as ArmorMitigationSystem/
// PeriodicDamageReductionSystem.
public static class BarrierSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<DamageRequest>(ApplyBarrier);
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void ApplyBarrier(DamageRequest request, ECS ecs)
    {
        if (request.Amount <= 0) return;

        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<BarrierComponent> barrierStore = ecs.GetComponentStore<BarrierComponent>();
        if (modifierStore == null || barrierStore == null) return;

        modifierStore.ForEach((ulong modifierId) =>
        {
            // Already absorbed by an earlier barrier this same request — only one barrier
            // consumes a given hit.
            if (request.Amount <= 0) return;
            if (!barrierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != request.EntityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;

            ref BarrierComponent barrier = ref barrierStore.GetComponent(modifierId);
            if (barrier.HealthRemaining <= 0f) return;

            float barrierDamage = request.Amount * barrier.DamageMultiplier + barrier.DamageAdditiveBonus;
            barrier.HealthRemaining = Mathf.Max(0f, barrier.HealthRemaining - barrierDamage);
            ecs.Delta.MarkComponentDirty(modifierId, typeof(BarrierComponent));

            request.Amount = 0;

            if (barrier.HealthRemaining <= 0f)
            {
                // Ends the modifier one tick early rather than waiting for its own
                // TicksRemaining to run out on its own — see ModifierSystem, which deletes
                // the entity (server-only) once TicksRemaining hits 0. Setting it straight to
                // 0 here wouldn't trigger that (ModifierSystem's own loop skips an already-
                // zero TicksRemaining without ever adding it to the expired list), so this
                // sets 1 instead and lets ModifierSystem's normal per-tick decrement carry it
                // the rest of the way next tick.
                ref ModifierComponent modifier = ref modifierStore.GetComponent(modifierId);
                if (modifier.TicksRemaining > 1)
                {
                    modifier.TicksRemaining = 1;
                    ecs.Delta.MarkComponentDirty(modifierId, typeof(ModifierComponent));
                }
            }
        });
    }
}
