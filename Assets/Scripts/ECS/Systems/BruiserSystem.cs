using UnityEngine;

// Reusable "defer a fraction of incoming damage, bleed it out over time instead" mitigation:
// subscribes to DamageRequest and, for every active BruiserComponent-carrying modifier
// targeting the hit entity, diverts DeferralRatio of the (already fully-mitigated) Amount
// into StoredDamage rather than letting it through immediately, and stamps LastHitTick.
//
// Every tick, Execute then drains up to DrainPerSecond worth of any nonzero StoredDamage
// straight onto the target's HealthComponent — bypassing DamageRequest entirely, unlike
// DamageOverTimeSystem's usual "fresh DamageRequest per proc" convention. This is
// deliberate: StoredDamage was already fully mitigated once when it was banked, so running
// the drain back through the request pipeline (including this same system's own deferral
// subscriber) would just re-defer another DeferralRatio of it forever instead of ever
// actually draining out. A DamageDealtEvent is still raised manually so damage-number VFX
// behaves the same as a real hit.
//
// If the target goes ClearAfterSeconds without taking a fresh hit, the remaining
// StoredDamage is wiped instead of continuing to bleed out — a Bruiser troop that survives a
// fight shouldn't keep dying to chip damage from a fight that's already over.
public static class BruiserSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private const float ClearAfterSeconds = 5f;

    private static void Setup(ECS ecs) => ecs.Requests.Subscribe<DamageRequest>(DeferDamage);

    private static void DeferDamage(DamageRequest request, ECS ecs)
    {
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
                bruiser.DrainCarry = 0f;
                ecs.Delta.MarkComponentDirty(modifierId, typeof(BruiserComponent));
                return;
            }

            ulong targetId = modifierStore.GetComponent(modifierId).TargetEntityId;
            if (!healthStore.HasComponent(targetId)) return;

            float drainAmount = Mathf.Min(bruiser.DrainPerSecond * TickManager.TickInterval, bruiser.StoredDamage);
            bruiser.StoredDamage -= drainAmount;
            bruiser.DrainCarry += drainAmount;

            int wholeDamage = Mathf.FloorToInt(bruiser.DrainCarry);
            bruiser.DrainCarry -= wholeDamage;
            ecs.Delta.MarkComponentDirty(modifierId, typeof(BruiserComponent));

            if (wholeDamage <= 0) return;

            ref HealthComponent health = ref healthStore.GetComponent(targetId);
            health.CurrentHealth -= wholeDamage;
            ecs.Delta.MarkComponentDirty(targetId, typeof(HealthComponent));

            PositionQuery.TryGet(ecs, targetId, out float x, out float y);
            ecs.FlagEvents.Add(new DamageDealtEvent
            {
                EntityId = targetId,
                DealerEntityId = DamageRequest.NO_DEALER_ENTITYID,
                Amount = wholeDamage,
                X = x,
                Y = y,
            });
        });
    }
}
