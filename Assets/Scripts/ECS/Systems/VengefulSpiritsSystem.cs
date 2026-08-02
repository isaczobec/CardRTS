using UnityEngine;

// Purely event-driven — no per-tick work of its own. Subscribes to DamageRequest's
// "executed" notification (mirrors GiantsbaneSystem/CleaveSystem's own shape) and, once a
// DIRECT hit is confirmed (see DamageProcType — a Secondary/DamageOverTime instance never
// procs this, including the very seeking projectile this system itself fires, which is
// tagged Secondary), visits EVERY active VengefulSpiritsSourceComponent-carrying modifier
// targeting the DEALER (see ModifierQuery.ForEachActiveModifierId — buying Vengeful Spirits
// more than once on the same card would create a separate modifier entity per purchase, but
// see VengefulSpiritsUpgrade.MaxStackCount, which caps that at 1) and fires a single homing
// projectile from the dealer's current position at whoever was just hit, dealing a flat
// amount of damage regardless of the dealer's own Damage stat (see
// SeekingProjectileComponent.FixedDamageOverride).
public static class VengefulSpiritsSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        if (request.ProcType != DamageProcType.Direct) return;
        if (request.Amount <= 0) return;

        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ModifierQuery.ForEachActiveModifierId<VengefulSpiritsSourceComponent>(ecs, dealerId, modifierId =>
            FireSpirit(ecs, dealerId, modifierId, request.EntityId));
    }

    private static void FireSpirit(ECS ecs, ulong dealerId, ulong modifierId, ulong targetId)
    {
        ComponentStore<VengefulSpiritsSourceComponent> sourceStore = ecs.GetComponentStore<VengefulSpiritsSourceComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (sourceStore == null || posStore == null) return;
        if (!posStore.HasComponent(dealerId)) return;

        VengefulSpiritsSourceComponent source = sourceStore.GetComponent(modifierId);
        PositionComponent pos = posStore.GetComponent(dealerId);

        ProjectilePool.Fire(ecs, source.ProjectilePoolOwnerId, targetId, new Vector2(pos.X, pos.Y));
    }
}
