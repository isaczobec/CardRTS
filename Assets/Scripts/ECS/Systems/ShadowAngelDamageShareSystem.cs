using System.Collections.Generic;
using UnityEngine;

// Redirects a fraction of a Shadow Angel's own incoming damage onto whichever nearby
// friendly troops currently carry its ShadowAngelDamageShareComponent aura buff (see
// ShadowAngelCard's self-buff, granted via PeriodicAreaEffectSystem/AreaEffectType.
// ShadowShield). Subscribed to DamageRequest last among every other DamageRequest
// subscriber (see TickManager's registration order — Subscribe callbacks fire in
// registration order) so it acts on the final, fully-mitigated Amount, right before
// DamageResolutionSystem's Flush actually applies whatever's left to the Angel's own
// HealthComponent.
//
// Splits the shared portion evenly across every currently active recipient (any remainder
// from integer division goes to the first few, arbitrarily) rather than giving each
// recipient the full share, and creates a brand new DamageRequest per recipient — sent back
// through the full request pipeline (armor, other mitigation, etc.) exactly like any other
// hit, rather than applied directly, so each recipient's own defenses still apply to their
// share. If no recipient is currently active (e.g. no friendly troop is in range right now),
// the Angel simply takes the full hit — there's nowhere to redirect it to.
//
// Each Angel redistributes at most once per tick (ShadowAngelComponent.
// HasRedistributedDamageThisTick, reset every tick by ShadowAngelTickResetSystem) — see that
// component's own doc comment for why: without this cap, two Angels within each other's aura
// range can ping-pong a single hit back and forth forever, since integer rounding can get
// stuck at a nonzero fixed point instead of ever converging to 0.
public static class ShadowAngelDamageShareSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static readonly List<ulong> _recipients = new List<ulong>();

    private static void Setup(ECS ecs) => ecs.Requests.Subscribe<DamageRequest>(RedirectDamage);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void RedirectDamage(DamageRequest request, ECS ecs)
    {
        if (request.PreMitigated) return;
        if (request.Amount <= 0) return;

        ComponentStore<ShadowAngelComponent> angelStore = ecs.GetComponentStore<ShadowAngelComponent>();
        bool isAngel = angelStore != null && angelStore.HasComponent(request.EntityId);
        if (isAngel && angelStore.GetComponent(request.EntityId).HasRedistributedDamageThisTick) return;

        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<ShadowAngelDamageShareComponent> shareStore = ecs.GetComponentStore<ShadowAngelDamageShareComponent>();
        if (modifierStore == null || shareStore == null) return;

        _recipients.Clear();
        float shareRatio = 0f;

        shareStore.ForEach((ulong modifierId) =>
        {
            ShadowAngelDamageShareComponent share = shareStore.GetComponent(modifierId);
            if (share.SourceEntityId != request.EntityId) return;
            if (!modifierStore.HasComponent(modifierId)) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;

            _recipients.Add(modifierStore.GetComponent(modifierId).TargetEntityId);
            shareRatio = share.ShareRatio;
        });

        if (_recipients.Count == 0) return;

        int totalShared = Mathf.Min(request.Amount, Mathf.RoundToInt(request.Amount * shareRatio));
        if (totalShared <= 0) return;

        request.Amount -= totalShared;

        // Marked BEFORE creating the new requests below — those may themselves route back
        // through this same callback against this same Angel later this tick (directly, or
        // via a chain through other Angels), and the flag needs to already be set by then.
        if (isAngel)
        {
            ref ShadowAngelComponent angel = ref angelStore.GetComponent(request.EntityId);
            angel.HasRedistributedDamageThisTick = true;
            ecs.Delta.MarkComponentDirty(request.EntityId, typeof(ShadowAngelComponent));
        }

        int perRecipient = totalShared / _recipients.Count;
        int remainder = totalShared - perRecipient * _recipients.Count;

        for (int i = 0; i < _recipients.Count; i++)
        {
            int amount = perRecipient + (i < remainder ? 1 : 0);
            if (amount <= 0) continue;

            ecs.Requests.CreateRequest(new DamageRequest(_recipients[i], amount)
            {
                DealerEntityId = request.DealerEntityId,
                Type = request.Type,
            });
        }
    }
}
