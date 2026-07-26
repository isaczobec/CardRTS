using UnityEngine;

// Reusable distance-based damage mitigation: subscribes to DamageRequest and, for every
// active DeflectionComponent-carrying modifier targeting the hit entity, reduces the
// (already armor-mitigated) Amount based on how far away the attacker was — 0% at MinRange
// or closer, scaling linearly up to MaxReductionRatio at MaxRange or further. A hit with no
// dealer (DamageRequest.NO_DEALER_ENTITYID, e.g. environmental damage) or a dealer that no
// longer has a PositionComponent is left unmitigated, since there's no distance to measure.
//
// Stateless (everything it needs lives in component data), so it's a single shared
// GlobalSystem — same shape as ArmorMitigationSystem/PeriodicDamageReductionSystem.
public static class DeflectionSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs) => ecs.Requests.Subscribe<DamageRequest>(ApplyDeflection);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void ApplyDeflection(DamageRequest request, ECS ecs)
    {
        if (request.Amount <= 0) return;
        if (request.DealerEntityId == 0 || request.DealerEntityId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(request.DealerEntityId) || !posStore.HasComponent(request.EntityId)) return;

        ComponentStore<DeflectionComponent> deflectionStore = ecs.GetComponentStore<DeflectionComponent>();
        if (deflectionStore == null) return;

        PositionComponent dealerPos = posStore.GetComponent(request.DealerEntityId);
        PositionComponent targetPos = posStore.GetComponent(request.EntityId);
        float distance = Vector2.Distance(new Vector2(dealerPos.X, dealerPos.Y), new Vector2(targetPos.X, targetPos.Y));

        ModifierQuery.ForEachActiveModifierId<DeflectionComponent>(ecs, request.EntityId, modifierId =>
        {
            DeflectionComponent deflection = deflectionStore.GetComponent(modifierId);
            if (deflection.MaxRange <= deflection.MinRange) return;

            float t = Mathf.Clamp01((distance - deflection.MinRange) / (deflection.MaxRange - deflection.MinRange));
            float reductionRatio = t * deflection.MaxReductionRatio;
            if (reductionRatio <= 0f) return;

            request.Amount = Mathf.Max(0, Mathf.RoundToInt(request.Amount * (1f - reductionRatio)));
        });
    }
}
