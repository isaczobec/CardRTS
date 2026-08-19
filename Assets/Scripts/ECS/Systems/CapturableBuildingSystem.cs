// "Spawn crystal" CapturableBuildingComponent buildings and the player base behind them have
// three behaviors:
//
//  - Crystal damage rules (Subscribe<DamageRequest>, pre-execute — see
//    CapturableBuildingQuery.CanDamageCrystal for the actual rules): whether a hit against a
//    crystal is allowed at all depends on whose bridge it's on, which of that bridge's two
//    crystals it is, and how much of the ATTACKER's own home bridge they currently hold.
//  - Base invulnerability (Subscribe<DamageRequest>, pre-execute): a player's base takes 0
//    damage while that player still owns at least one crystal on their own home bridge.
//  - Instant capture on death (Subscribe<DeathRequest> pre-execute veto + SubscribeExecuted
//    post-execute revive — same two-hook shape RespawnSystem uses for its own timer-based
//    respawn, see that class's own doc comment): rather than actually dying, the crystal is
//    immediately revived at HALF health (explicit design ask) and its ownership flips to
//    whoever landed the killing blow (HealthComponent.LastDamageDealer's own owner) — no
//    cooldown, no RespawnableInPlaceComponent, unlike every other respawning neutral entity
//    in the game.
public static class CapturableBuildingSystem
{
    // Flat gold bounty for capturing (killing) another player's crystal — explicit design ask.
    private const int CrystalCaptureGoldReward = 200;

    public static readonly GlobalSystem Instance = new GlobalSystem((ecs, flagEvents) => { }, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<DamageRequest>(ApplyCrystalDamageRule);
        ecs.Requests.Subscribe<DamageRequest>(ApplyBaseInvulnerability);
        ecs.Requests.Subscribe<DeathRequest>(VetoDeletion);
        ecs.Requests.SubscribeExecuted<DeathRequest>(OnDeathExecuted);
    }

    private static void ApplyCrystalDamageRule(DamageRequest request, ECS ecs)
    {
        if (request.PreMitigated) return;
        if (request.DealerEntityId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();
        if (capturableStore == null || !capturableStore.HasComponent(request.EntityId)) return;

        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(request.DealerEntityId)) return;

        ushort attackerPlayerId = troopStore.GetComponent(request.DealerEntityId).OwnerPlayerId;
        if (attackerPlayerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return;

        CapturableBuildingComponent target = capturableStore.GetComponent(request.EntityId);
        if (!CapturableBuildingQuery.CanDamageCrystal(ecs, attackerPlayerId, target))
            request.Cancel();
    }

    // A player's base is fully shielded as long as they still hold ground on their own
    // bridge — explicit design ask ("if the player owns any of the spawn crystals on their
    // own bridge, all damage against their base should be reduced to 0").
    private static void ApplyBaseInvulnerability(DamageRequest request, ECS ecs)
    {
        if (request.PreMitigated) return;
        if (request.DealerEntityId == DamageRequest.NO_DEALER_ENTITYID) return;

        ComponentStore<RenderableComponent> renderableStore = ecs.GetComponentStore<RenderableComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (renderableStore == null || troopStore == null) return;
        if (!renderableStore.HasComponent(request.EntityId) || renderableStore.GetComponent(request.EntityId).Type != RenderableType.PlayerBaseCore) return;
        if (!troopStore.HasComponent(request.EntityId)) return;

        ushort baseOwnerId = troopStore.GetComponent(request.EntityId).OwnerPlayerId;
        if (CapturableBuildingQuery.OwnsAnyOwnBridgeCrystal(ecs, baseOwnerId))
            request.Cancel();
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
    // DamageRequest with a real dealer can reduce a building to 0 HP) leaves the crystal
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
        ushort previousOwnerId = troop.OwnerPlayerId;
        troop.IsDead = false;
        troop.OwnerPlayerId = newOwnerId;
        ecs.Delta.MarkComponentDirty(request.EntityId, typeof(TroopComponent));

        // Gold bounty for capturing another player's crystal — explicit design ask ("killing
        // another player's respawn crystal should... drop 200 gold"). Only fires when it was
        // actually held by a DIFFERENT real player at the moment of death (never neutral, and
        // never the killer's own — CanDamageCrystal already blocks friendly fire, but the
        // check costs nothing) and a real killer resolved above.
        if (previousOwnerId != TroopComponent.NEUTRAL_OWNER_PLAYER_ID &&
            previousOwnerId != newOwnerId &&
            newOwnerId != TroopComponent.NEUTRAL_OWNER_PLAYER_ID)
        {
            ulong killerResourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, newOwnerId);
            if (killerResourceEntityId != 0)
                ecs.Requests.CreateRequest(new ResourcesAdded(killerResourceEntityId, ResourceType.Gold, CrystalCaptureGoldReward));
        }

        ref SelectableComponent selectable = ref selectableStore.GetComponent(request.EntityId);
        selectable.OwnerPlayerId = newOwnerId;
        ecs.Delta.MarkComponentDirty(request.EntityId, typeof(SelectableComponent));

        // Half health on respawn — explicit design ask (a plain capture elsewhere in the
        // codebase fully heals; this one deliberately doesn't).
        ref HealthComponent health = ref healthStore.GetComponent(request.EntityId);
        health.CurrentHealth = statsStore.GetComponent(request.EntityId).MaxHealth / 2;
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
