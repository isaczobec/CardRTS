using UnityEngine;

// Purely event-driven — no per-tick work of its own. Subscribes to DamageRequest's "executed"
// notification (mirrors OnHitScheduleSystem's own shape) and, once a hit is confirmed, visits
// EVERY active GiantsbaneComponent-carrying modifier targeting the DEALER (see
// ModifierQuery.ForEachActiveModifierId — buying the Giantsbane upgrade more than once on the
// same card creates a separate modifier entity per purchase, see CardUpgrade/UpgradeQuery, so
// each one procs independently rather than only the first found) and, for each, counts it
// against its OWN PeriodHits/HitsUntilProc and, once that countdown reaches 0, deals bonus
// damage to whatever was just hit equal to BonusDamageMaxHealthRatio x that target's own max
// health, via a SEPARATE new DamageRequest (not just inflating the original hit's Amount) so
// it goes through the normal armor-mitigation pipeline as its own distinct instance, same as
// any other hit — explicit design ask ("not pure damage, create a new damage request"). That
// bonus hit is itself dealt by the same entity and so counts toward the NEXT proc's own hit
// count for every stack — a minor, harmless quirk (procs very slightly more often than exactly
// every Nth externally-caused hit), not worth special-casing out.
public static class GiantsbaneSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ModifierQuery.ForEachActiveModifierId<GiantsbaneComponent>(ecs, dealerId, modifierId =>
            ProcStack(ecs, dealerId, modifierId, request.EntityId));
    }

    private static void ProcStack(ECS ecs, ulong dealerId, ulong modifierId, ulong targetId)
    {
        ComponentStore<GiantsbaneComponent> store = ecs.GetComponentStore<GiantsbaneComponent>();
        ref GiantsbaneComponent giantsbane = ref store.GetComponent(modifierId);

        giantsbane.HitsUntilProc--;
        if (giantsbane.HitsUntilProc > 0)
        {
            ecs.Delta.MarkComponentDirty(modifierId, typeof(GiantsbaneComponent));
            return;
        }

        giantsbane.HitsUntilProc = Mathf.Max(1, giantsbane.PeriodHits);
        ecs.Delta.MarkComponentDirty(modifierId, typeof(GiantsbaneComponent));

        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore == null || !healthStore.HasComponent(targetId)) return;

        int currentHealth = healthStore.GetComponent(targetId).CurrentHealth;
        int maxHealth = StatsQuery.GetMaxHealth(ecs, targetId, currentHealth);
        int bonusDamage = Mathf.RoundToInt(maxHealth * giantsbane.BonusDamageMaxHealthRatio);
        if (bonusDamage <= 0) return;

        ecs.Requests.CreateRequest(new DamageRequest(targetId, bonusDamage)
        {
            DealerEntityId = dealerId,
            ProcType       = DamageProcType.Secondary,
        });
    }
}
