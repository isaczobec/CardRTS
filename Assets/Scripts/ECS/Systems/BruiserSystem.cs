using UnityEngine;

// Reusable "defer a fraction of incoming damage, bleed it out over time instead" mitigation:
// subscribes to DamageRequest and, for every active BruiserComponent-carrying modifier
// targeting the hit entity, diverts DeferralRatio of the (already fully-mitigated) Amount
// into StoredDamage rather than letting it through immediately, and stamps LastHitTick.
//
// Once a second (TicksUntilNextDrain, same cadence pattern as DamageOverTimeComponent's own
// TicksUntilNextProc), Execute then drains up to DrainPerSecond worth of any nonzero
// StoredDamage in one lump via a fresh DamageRequest, same convention as DamageOverTimeSystem.
// That request is created with PreMitigated = true (see DamageRequest's own doc comment) so
// it skips every mitigation/redirection subscriber — including this same system's own
// deferral callback below, which would otherwise re-defer another DeferralRatio of it forever
// instead of ever actually draining out — and applies Amount straight to HealthComponent,
// same as if it had been dealt directly.
//
// If the target goes ClearAfterSeconds without taking a fresh hit, the remaining
// StoredDamage is wiped instead of continuing to bleed out — a Bruiser troop that survives a
// fight shouldn't keep dying to chip damage from a fight that's already over.
public static class BruiserSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private const float ClearAfterSeconds = 5f;
    private static readonly int DrainPeriodTicks = TickManager.SecondsToTicks(1f);

    private static void Setup(ECS ecs) => ecs.Requests.Subscribe<DamageRequest>(DeferDamage);

    private static void DeferDamage(DamageRequest request, ECS ecs)
    {
        if (request.PreMitigated) return;

        ComponentStore<BruiserComponent> bruiserStore = ecs.GetComponentStore<BruiserComponent>();
        if (bruiserStore == null) return;

        ModifierQuery.ForEachActiveModifierId<BruiserComponent>(ecs, request.EntityId, modifierId =>
        {
            ref BruiserComponent bruiser = ref bruiserStore.GetComponent(modifierId);
            bruiser.LastHitTick = ecs.CurrentSimulationTick;

            if (request.Amount > 0)
            {
                int deferred = Mathf.Min(request.Amount, Mathf.RoundToInt(request.Amount * bruiser.DeferralRatio));
                if (deferred > 0)
                {
                    if (bruiser.StoredDamage <= 0f)
                        bruiser.TicksUntilNextDrain = DrainPeriodTicks;
                    request.Amount -= deferred;
                    bruiser.StoredDamage += deferred;
                }
            }

            ecs.Delta.MarkComponentDirty(modifierId, typeof(BruiserComponent));
        });
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<BruiserComponent> bruiserStore = ecs.GetComponentStore<BruiserComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (modifierStore == null || bruiserStore == null || healthStore == null) return;

        int clearAfterTicks = TickManager.SecondsToTicks(ClearAfterSeconds);

        bruiserStore.ForEach((ulong modifierId) =>
        {
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;

            ref BruiserComponent bruiser = ref bruiserStore.GetComponent(modifierId);
            if (bruiser.StoredDamage <= 0f) return;

            long ticksSinceHit = (long)ecs.CurrentSimulationTick - (long)bruiser.LastHitTick;
            if (ticksSinceHit < 0 || ticksSinceHit >= clearAfterTicks)
            {
                bruiser.StoredDamage = 0f;
                ecs.Delta.MarkComponentDirty(modifierId, typeof(BruiserComponent));
                return;
            }

            bruiser.TicksUntilNextDrain--;
            if (bruiser.TicksUntilNextDrain > 0)
            {
                ecs.Delta.MarkComponentDirty(modifierId, typeof(BruiserComponent));
                return;
            }
            bruiser.TicksUntilNextDrain = DrainPeriodTicks;

            ulong targetId = modifierStore.GetComponent(modifierId).TargetEntityId;
            if (!healthStore.HasComponent(targetId))
            {
                ecs.Delta.MarkComponentDirty(modifierId, typeof(BruiserComponent));
                return;
            }

            int drainAmount = Mathf.Min(Mathf.RoundToInt(bruiser.DrainPerSecond), Mathf.CeilToInt(bruiser.StoredDamage));
            bruiser.StoredDamage -= drainAmount;
            ecs.Delta.MarkComponentDirty(modifierId, typeof(BruiserComponent));

            if (drainAmount <= 0) return;

            ecs.Requests.CreateRequest(new DamageRequest(targetId, drainAmount)
            {
                DealerEntityId = DamageRequest.NO_DEALER_ENTITYID,
                PreMitigated = true,
            });
        });
    }
}
