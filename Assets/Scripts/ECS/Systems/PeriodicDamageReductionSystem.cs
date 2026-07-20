using UnityEngine;

// Deterministically reduces a troop's incoming damage by ReductionRatio on every
// HitInterval-th hit it takes, per every currently-active PeriodicDamageReductionComponent
// modifier targeting it (mirrors StatModifierSystem's own "walk every ModifierComponent
// targeting this entity" shape, rather than reading the payload straight off the target —
// see PeriodicDamageReductionComponent's doc comment for why it lives on a modifier entity).
// Subscribed to DamageRequest after ArmorMitigationSystem (see TickManager's registration
// order — Subscribe callbacks fire in registration order), so HitsTaken counts every hit
// that survives mitigation/cancellation, and ReductionRatio is applied on top of the
// already-mitigated Amount. A request cancelled by an earlier subscriber (e.g. an
// invulnerability effect) never reaches this callback at all, so it doesn't count as a hit.
// Registered as a GlobalSystem purely for the Setup hook, same as ArmorMitigationSystem.
public static class PeriodicDamageReductionSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<DamageRequest>(ApplyReduction);
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void ApplyReduction(DamageRequest request, ECS ecs)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<PeriodicDamageReductionComponent> reductionStore = ecs.GetComponentStore<PeriodicDamageReductionComponent>();
        if (modifierStore == null || reductionStore == null) return;

        modifierStore.ForEach((ulong modifierId) =>
        {
            if (!reductionStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != request.EntityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;

            ref PeriodicDamageReductionComponent reduction = ref reductionStore.GetComponent(modifierId);
            if (reduction.HitInterval <= 0) return;

            reduction.HitsTaken++;
            bool triggers = reduction.HitsTaken % reduction.HitInterval == 0;
            ecs.Delta.MarkComponentDirty(modifierId, typeof(PeriodicDamageReductionComponent));

            if (!triggers) return;

            request.Amount = Mathf.Max(0, Mathf.RoundToInt(request.Amount * (1f - reduction.ReductionRatio)));
            ecs.FlagEvents.Add(new PeriodicDamageReductionProcEvent { EntityId = request.EntityId });
        });
    }
}
