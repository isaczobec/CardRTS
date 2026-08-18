using UnityEngine;

// CapturableBuildingComponent buildings (see CapturableBuildingFeature) have two behaviors:
//
//  - Attack-range damage reduction (Subscribe<DamageRequest>, pre-execute — see
//    CapturableBuildingQuery.GetDamageMultiplier for the actual rules): fully blocked unless
//    the attacker already owns an adjacent capturable building or the target is directly
//    adjacent to the attacker's own base, and even then only half damage unless it IS directly
//    adjacent to the attacker's own base.
//  - Instant capture on death (Subscribe<DeathRequest> pre-execute veto + SubscribeExecuted
//    post-execute revive — same two-hook shape RespawnSystem uses for its own timer-based
//    respawn, see that class's own doc comment): rather than actually dying, the building is
//    immediately healed back to full and its ownership flips to whoever landed the killing
//    blow (HealthComponent.LastDamageDealer's own owner) — no cooldown, no
//    RespawnableInPlaceComponent, unlike every other respawning neutral entity in the game.
public static class CapturableBuildingSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem((ecs, flagEvents) => { }, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<DamageRequest>(ApplyCaptureRangeMitigation);
        ecs.Requests.Subscribe<DeathRequest>(VetoDeletion);
        ecs.Requests.SubscribeExecuted<DeathRequest>(OnDeathExecuted);
    }

    private static void ApplyCaptureRangeMitigation(DamageRequest request, ECS ecs)
    {
        if (request.PreMitigated) return;
        if (request.DealerEntityId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();
        if (capturableStore == null || !capturableStore.HasComponent(request.EntityId)) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(request.DealerEntityId)) return;

        ushort attackerPlayerId = troopStore.GetComponent(request.DealerEntityId).OwnerPlayerId;
        if (attackerPlayerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return;

        CapturableBuildingComponent capturable = capturableStore.GetComponent(request.EntityId);
        float multiplier = CapturableBuildingQuery.GetDamageMultiplier(ecs, attackerPlayerId, capturable.IslandRegionId, capturable.IsMidBridge);

        if (multiplier <= 0f)
        {
            request.Cancel();
            return;
        }
        if (multiplier < 1f)
            request.Amount = Mathf.Max(0, Mathf.RoundToInt(request.Amount * multiplier));
    }

    // Pre-execute: this entity must survive DeathRequest.Execute's own final ecs.DeleteEntity
    // call so OnDeathExecuted below has something left to revive — same ShouldDelete veto
    // RespawnSystem's own pre-execute Subscribe<DeathRequest> callback uses for
    // RespawnableInPlaceComponent entities.
    private static void VetoDeletion(DeathRequest request, ECS ecs)
    {
        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();
        if (capturableStore == null || !capturableStore.HasComponent(request.EntityId)) return;

        request.ShouldDelete = false;
    }

    // Post-execute: DeathRequest.Execute already set IsDead=true and fired TroopDiedEvent by
    // the time this runs (see RequestManager.NotifyExecuted's own timing) — undone here,
    // synchronously, in the same tick, so no other system ever observes this entity as
    // actually dead. HealthComponent.LastDamageDealer (set by DamageRequest.Execute right
    // before DeathSystem ever notices CurrentHealth <= 0) identifies the capturing player; a
    // dealer that no longer exists or isn't a troop (shouldn't normally happen, since only a
    // DamageRequest with a real dealer can reduce a building to 0 HP) leaves the building
    // neutral rather than guessing an owner.
    private static void OnDeathExecuted(DeathRequest request, ECS ecs)
    {
        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();
        if (capturableStore == null || !capturableStore.HasComponent(request.EntityId)) return;

        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<SelectableComponent> selectableStore = ecs.GetComponentStore<SelectableComponent>();
        ComponentStore<StatsComponent> statsStore = ecs.GetComponentStore<StatsComponent>();
        if (healthStore == null || troopStore == null || selectableStore == null || statsStore == null) return;
        if (!healthStore.HasComponent(request.EntityId) || !troopStore.HasComponent(request.EntityId) ||
            !selectableStore.HasComponent(request.EntityId) || !statsStore.HasComponent(request.EntityId))
            return;

        ulong killerEntityId = healthStore.GetComponent(request.EntityId).LastDamageDealer;
        ushort newOwnerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID;
        if (killerEntityId != 0 && killerEntityId != DamageRequest.NO_DEALER_ENTITYID && troopStore.HasComponent(killerEntityId))
            newOwnerId = troopStore.GetComponent(killerEntityId).OwnerPlayerId;

        ref TroopComponent troop = ref troopStore.GetComponent(request.EntityId);
        troop.IsDead = false;
        troop.OwnerPlayerId = newOwnerId;
        ecs.Delta.MarkComponentDirty(request.EntityId, typeof(TroopComponent));

        ref SelectableComponent selectable = ref selectableStore.GetComponent(request.EntityId);
        selectable.OwnerPlayerId = newOwnerId;
        ecs.Delta.MarkComponentDirty(request.EntityId, typeof(SelectableComponent));

        ref HealthComponent health = ref healthStore.GetComponent(request.EntityId);
        health.CurrentHealth = statsStore.GetComponent(request.EntityId).MaxHealth;
        ecs.Delta.MarkComponentDirty(request.EntityId, typeof(HealthComponent));

        // This building's HealthComponent was just reset directly (no DamageRequest/
        // HealRequest involved), so anything that only refreshes off those — HealthBarManager
        // above all, which otherwise keeps showing the 0 HP the killing blow last set it to —
        // never hears about it. Firing the same event RespawnSystem.Execute fires after ITS
        // own equivalent reset is what every one of those listeners already reacts to (health
        // bar refresh, selection ring/minimap dot un-hide, etc.); every listener no-ops
        // gracefully for an entity that isn't actually RespawnableInPlaceComponent-based
        // (see e.g. HealthBarManager.OnRespawnableEntityRespawned), so this is safe here too.
        PositionQuery.TryGet(ecs, request.EntityId, out float x, out float y);
        ecs.FlagEvents.Add(new RespawnableEntityRespawnedEvent { EntityId = request.EntityId, X = x, Y = y });
    }
}
