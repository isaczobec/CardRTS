using System.Collections.Generic;

// Once a player's base (RenderableType.PlayerBaseCore, owned by that player — see
// SpawnPlayerBasesFeature/PlayerBaseQuery for the same identification pattern) dies, that
// player is permanently eliminated: every OTHER entity they own is destroyed, and
// PlayerComponent.IsEliminated is set on their player entity — see PlayerEliminationQuery,
// read by ECS.GetInputsForTick (drops all of their input from then on, server-side and on
// every client's own ClientLocalECS alike, since combat/death is fully predicted client-side
// and this reacts to the exact same DeathRequest both already process identically) and
// InputBuffer.ShouldAcceptLocalInput (stops the local player from even enqueueing new input
// once it's their own base).
//
// Hooks DeathRequest's "executed" notification the same way OnDeathResourceDropSystem/
// ResourceProductionOnDeathSystem do (see RequestManager.SubscribeExecuted) — this runs after
// TroopComponent.IsDead is set and TroopDiedEvent fires, but strictly before DeathRequest.
// Execute's own subsequent ecs.DeleteEntity call for the base itself, so the base's
// RenderableComponent/TroopComponent are still readable here.
public static class PlayerEliminationSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem((ecs, flagEvents) => { }, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DeathRequest>(OnDeathExecuted);

    private static void OnDeathExecuted(DeathRequest request, ECS ecs)
    {
        ComponentStore<RenderableComponent> renderableStore = ecs.GetComponentStore<RenderableComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (renderableStore == null || troopStore == null) return;
        if (!renderableStore.HasComponent(request.EntityId) || renderableStore.GetComponent(request.EntityId).Type != RenderableType.PlayerBaseCore) return;
        if (!troopStore.HasComponent(request.EntityId)) return;

        ushort ownerPlayerId = troopStore.GetComponent(request.EntityId).OwnerPlayerId;
        Eliminate(ecs, ownerPlayerId, request.EntityId, troopStore);
    }

    private static void Eliminate(ECS ecs, ushort playerId, ulong dyingBaseEntityId, ComponentStore<TroopComponent> troopStore)
    {
        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        if (playerStore == null) return;

        ulong playerEntityId = 0;
        playerStore.ForEach((ulong id) =>
        {
            if (playerEntityId != 0) return;
            if (playerStore.GetComponent(id).PlayerId == playerId) playerEntityId = id;
        });
        if (playerEntityId == 0) return;

        // Idempotent — only the FIRST PlayerBaseCore death for this player actually
        // eliminates; a hypothetical later one (e.g. a second base granted by some future
        // card) is a no-op rather than re-sweeping entities that are already gone.
        ref PlayerComponent player = ref playerStore.GetComponent(playerEntityId);
        if (player.IsEliminated) return;
        player.IsEliminated = true;
        ecs.Delta.MarkComponentDirty(playerEntityId, typeof(PlayerComponent));

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        // Every other entity this player owns. The dying base itself (dyingBaseEntityId) is
        // deliberately excluded — DeathRequest.Execute deletes it right after this callback
        // returns (see ecs.NotifyRequestExecuted's own ordering), so deleting it a second time
        // here would just log a harmless-but-noisy "entity did not exist" error.
        //
        // A captured CapturableBuildingComponent building is excluded from deletion too —
        // otherwise the map's total supply of capturable objectives would permanently shrink
        // every time a player got eliminated. It reverts to neutral (full health) instead, via
        // RevertCapturedBuildingsToNeutral below, so it stays up for grabs for whoever's left.
        ComponentStore<CapturableBuildingComponent> capturableStore = ecs.GetComponentStore<CapturableBuildingComponent>();

        var toDelete = new List<ulong>();
        troopStore.ForEach((ulong id) =>
        {
            if (id == dyingBaseEntityId) return;
            if (troopStore.GetComponent(id).OwnerPlayerId != playerId) return;
            if (capturableStore != null && capturableStore.HasComponent(id)) return;
            toDelete.Add(id);
        });
        foreach (ulong id in toDelete)
            ecs.DeleteEntity(id);

        if (capturableStore != null)
            RevertCapturedBuildingsToNeutral(ecs, playerId, capturableStore, troopStore);

        // TornadoProjectileComponent carries its own OwnerPlayerId directly (it isn't a troop
        // — a one-off spell-effect hitbox, see that component's own doc comment) so it needs
        // its own separate sweep rather than being caught by the TroopComponent scan above.
        ComponentStore<TornadoProjectileComponent> tornadoStore = ecs.GetComponentStore<TornadoProjectileComponent>();
        if (tornadoStore != null)
        {
            var toDeleteTornadoes = new List<ulong>();
            tornadoStore.ForEach((ulong id) =>
            {
                if (tornadoStore.GetComponent(id).OwnerPlayerId == playerId) toDeleteTornadoes.Add(id);
            });
            foreach (ulong id in toDeleteTornadoes)
                ecs.DeleteEntity(id);
        }
    }

    // Resets every capturable building this eliminated player still owned back to neutral,
    // full health — the same "revert to neutral" shape CapturableBuildingSystem.OnDeathExecuted
    // uses for a plain combat capture, just triggered by the owner's elimination instead of the
    // building's own death.
    private static void RevertCapturedBuildingsToNeutral(ECS ecs, ushort playerId,
        ComponentStore<CapturableBuildingComponent> capturableStore, ComponentStore<TroopComponent> troopStore)
    {
        ComponentStore<SelectableComponent> selectableStore = ecs.GetComponentStore<SelectableComponent>();
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        ComponentStore<StatsComponent> statsStore = ecs.GetComponentStore<StatsComponent>();
        if (selectableStore == null || healthStore == null || statsStore == null) return;

        var toRevert = new List<ulong>();
        capturableStore.ForEach((ulong id) =>
        {
            if (troopStore.HasComponent(id) && troopStore.GetComponent(id).OwnerPlayerId == playerId)
                toRevert.Add(id);
        });

        foreach (ulong id in toRevert)
        {
            if (!selectableStore.HasComponent(id) || !healthStore.HasComponent(id) || !statsStore.HasComponent(id)) continue;

            ref TroopComponent troop = ref troopStore.GetComponent(id);
            troop.OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID;
            ecs.Delta.MarkComponentDirty(id, typeof(TroopComponent));

            ref SelectableComponent selectable = ref selectableStore.GetComponent(id);
            selectable.OwnerPlayerId = TroopComponent.NEUTRAL_OWNER_PLAYER_ID;
            ecs.Delta.MarkComponentDirty(id, typeof(SelectableComponent));

            ref HealthComponent health = ref healthStore.GetComponent(id);
            health.CurrentHealth = statsStore.GetComponent(id).MaxHealth;
            ecs.Delta.MarkComponentDirty(id, typeof(HealthComponent));
        }
    }
}
