using UnityEngine;

// Subscribes to DamageRequest (see RequestManager — every subscribed callback runs before
// Execute applies the damage) and boosts its Amount by BuildingDamageBonusComponent.
// BonusRatio whenever the dealer has an active modifier of that kind AND the request's
// target is a building (has BuildingComponent) — e.g. GoblinSnatcherCard's permanent +20%
// damage vs. buildings.
//
// Registered BEFORE ArmorMitigationSystem in TickManager (Subscribe callbacks run in
// registration order — see RequestManager's own doc comment), so this boosts the raw
// damage dealt first, the same order a "deals X% more damage" stat bonus would apply in,
// before the target's own Armor/SpellResist mitigates the (now-boosted) amount. Registered
// as a GlobalSystem purely for the Setup hook, same as ArmorMitigationSystem.
public static class BuildingDamageBonusSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<DamageRequest>(ApplyBonus);
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void ApplyBonus(DamageRequest request, ECS ecs)
    {
        if (request.DealerEntityId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<BuildingComponent> buildingStore = ecs.GetComponentStore<BuildingComponent>();
        if (buildingStore == null || !buildingStore.HasComponent(request.EntityId)) return;

        float bonusRatio = FindActiveBonusRatio(ecs, request.DealerEntityId);
        if (bonusRatio == 0f) return;

        request.Amount = Mathf.Max(0, Mathf.RoundToInt(request.Amount * (1f + bonusRatio)));
    }

    // Sums every active BuildingDamageBonusComponent targeting dealerId — mirrors
    // ProjectileOnHitSystem.FindActiveModifierId's "walk every ModifierComponent" shape,
    // generalized to sum every stacked instance instead of stopping at the first, since a
    // troop could in principle have more than one source of this bonus.
    private static float FindActiveBonusRatio(ECS ecs, ulong dealerId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<BuildingDamageBonusComponent> bonusStore = ecs.GetComponentStore<BuildingDamageBonusComponent>();
        if (modifierStore == null || bonusStore == null) return 0f;

        float total = 0f;
        bonusStore.ForEach((ulong modifierId) =>
        {
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != dealerId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;
            total += bonusStore.GetComponent(modifierId).BonusRatio;
        });
        return total;
    }
}
