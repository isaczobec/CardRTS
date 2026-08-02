using System.Collections.Generic;
using UnityEngine;

// Purely event-driven — no per-tick work of its own. Subscribes to DamageRequest's
// "executed" notification (mirrors GiantsbaneSystem's own shape) and, once a hit is
// confirmed, visits EVERY active CleaveComponent-carrying modifier targeting the DEALER (see
// ModifierQuery.ForEachActiveModifierId — buying Cleave more than once on the same card
// creates a separate modifier entity per purchase, see CardUpgrade/UpgradeQuery, so each one
// splashes independently) and, for each, deals SplashRatio x the ORIGINAL hit's own
// (already-mitigated) Amount to every OTHER enemy troop within Radius of whoever was
// actually hit, via a SEPARATE new DamageRequest per splash target (not just inflating the
// original hit) — same "goes through the normal armor-mitigation pipeline as its own
// distinct instance" reasoning as GiantsbaneSystem's own bonus damage.
//
// Unlike GiantsbaneSystem's bonus damage (which is harmless to have recursively re-trigger
// GiantsbaneSystem itself, since that system is gated by a per-stack hit-count cooldown),
// Cleave has no such gating — every damage instance splashes — so its own splash hits are
// tagged DamageRequest.ProcType = DamageProcType.Secondary, and OnDamageExecuted skips any
// request already tagged that way, to guarantee each ORIGINAL hit splashes exactly once
// instead of cascading across every enemy in a cluster.
public static class CleaveSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        if (request.ProcType == DamageProcType.Secondary) return; // see this class's own doc comment
        if (request.Amount <= 0) return;

        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ModifierQuery.ForEachActiveModifierId<CleaveComponent>(ecs, dealerId, modifierId =>
            ProcCleave(ecs, dealerId, modifierId, request.EntityId, request.Amount));
    }

    private static void ProcCleave(ECS ecs, ulong dealerId, ulong modifierId, ulong primaryTargetId, int damageAmount)
    {
        ComponentStore<CleaveComponent> cleaveStore = ecs.GetComponentStore<CleaveComponent>();
        CleaveComponent cleave = cleaveStore.GetComponent(modifierId);

        int splashDamage = Mathf.RoundToInt(damageAmount * cleave.SplashRatio);
        if (splashDamage <= 0) return;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (posStore == null || troopStore == null || healthStore == null) return;
        if (!posStore.HasComponent(primaryTargetId) || !troopStore.HasComponent(dealerId)) return;

        PositionComponent primaryPos = posStore.GetComponent(primaryTargetId);
        ushort dealerOwnerId = troopStore.GetComponent(dealerId).OwnerPlayerId;

        // Fired once per proc (regardless of whether any splash target actually ends up in
        // range), not once per individual splash hit — see CleaveActivatedEvent's own doc
        // comment.
        ecs.FlagEvents.Add(new CleaveActivatedEvent { EntityId = dealerId, X = primaryPos.X, Y = primaryPos.Y });

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(primaryPos.X, primaryPos.Y, cleave.Radius, _queryBuffer);

        foreach (ulong targetId in _queryBuffer)
        {
            if (targetId == primaryTargetId || targetId == dealerId) continue;
            if (!troopStore.HasComponent(targetId)) continue;
            if (troopStore.GetComponent(targetId).OwnerPlayerId == dealerOwnerId) continue;
            if (!healthStore.HasComponent(targetId)) continue;
            if (!ActivationQuery.IsActivated(ecs, targetId)) continue;

            ecs.Requests.CreateRequest(new DamageRequest(targetId, splashDamage)
            {
                DealerEntityId = dealerId,
                ProcType       = DamageProcType.Secondary,
            });
        }
    }
}
