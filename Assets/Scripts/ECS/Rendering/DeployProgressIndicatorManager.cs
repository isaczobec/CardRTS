using System.Collections.Generic;
using UnityEngine;

// Shows a flat ground disc under an entity while it's mid-deploy — from the moment a
// SpawnAtPointCard spawns it (every SpawnAtPointCard-spawned entity carries an
// ActivatableComponent — see ActivationSystem) until its activation delay finishes
// (EntityActivatedEvent) or it's removed early (EntityDeletedEvent). Visible only for the
// client whose card spawned it — this is player-facing UI, not server-confirmed game state
// other players need to see. Ownership is read off TroopComponent.OwnerPlayerId; every
// SpawnAtPointCard-spawned entity carries one purely for this (see TroopCardHelper/
// BuildingSpawnHelper/AoeSpellCard) even when it isn't a troop in any other sense.
//
// Separate from the ECS architecture, like CardRangeIndicatorManager — never registered as
// an ISystem, just polls the live ECS each frame to update each indicator's fill. Deploying
// troops/buildings never move (TroopCardHelper/BuildingSpawnHelper both leave a fresh
// entity's destination equal to its spawn position, and ActivationQuery.CanTakeActions —
// which gates PathfindingSystem's player-order handling — is false until activation
// finishes), so like CardRangeIndicatorManager's building rings, each indicator's position
// is only ever set once, at creation.
public class DeployProgressIndicatorManager : Singleton<DeployProgressIndicatorManager>
{
    [SerializeField] private DeployProgressIndicatorPrefab _indicatorPrefab;

    private ECS _ecs;
    private ComponentStore<ActivatableComponent> _activatableStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<PositionComponent> _positionStore;

    private readonly Dictionary<ulong, DeployProgressIndicatorPrefab> _indicators = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<ActivatableComponent>>(OnActivatableAdded);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnEntityActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);

        _ecs = TickManager.instance.ActiveECS;
        _activatableStore = _ecs.GetComponentStore<ActivatableComponent>();
        _troopStore = _ecs.GetComponentStore<TroopComponent>();
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        foreach (KeyValuePair<ulong, DeployProgressIndicatorPrefab> kvp in _indicators)
        {
            ulong entityId = kvp.Key;
            DeployProgressIndicatorPrefab indicator = kvp.Value;

            if (!_activatableStore.HasComponent(entityId))
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            ActivatableComponent activatable = _activatableStore.GetComponent(entityId);
            if (activatable.InitialTicksUntilActive == 0)
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            indicator.gameObject.SetActive(true);
            indicator.SetProgress((float)activatable._ticksUntilActive / activatable.InitialTicksUntilActive);
        }
    }

    // ── Indicator lifecycle — one per deploying entity owned by the local client. ──────

    private void OnActivatableAdded(ComponentAddedEvent<ActivatableComponent> e)
    {
        if (_indicators.ContainsKey(e.EntityId)) return;
        if (_activatableStore == null || !_activatableStore.HasComponent(e.EntityId)) return;
        if (!OwnedByLocalPlayer(e.EntityId)) return;

        ActivatableComponent activatable = _activatableStore.GetComponent(e.EntityId);
        if (activatable.InitialTicksUntilActive == 0) return; // no deploy delay — nothing to show
        if (_positionStore == null || !_positionStore.HasComponent(e.EntityId)) return;
        if (_indicatorPrefab == null) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        DeployProgressIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(pos), Quaternion.identity, transform);
        indicator.name = $"DeployProgressIndicator_{e.EntityId}";
        indicator.SetProgress(1f);

        _indicators[e.EntityId] = indicator;
    }

    private void OnEntityActivated(EntityActivatedEvent e) => DestroyIndicator(e.EntityId);
    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyIndicator(e.EntityId);

    private void DestroyIndicator(ulong entityId)
    {
        if (!_indicators.TryGetValue(entityId, out DeployProgressIndicatorPrefab indicator)) return;
        Destroy(indicator.gameObject);
        _indicators.Remove(entityId);
    }

    // Ownership isn't part of ActivatableComponent itself — every SpawnAtPointCard-spawned
    // entity carries a TroopComponent for OwnerPlayerId regardless of what kind of entity
    // it actually is (see the class doc comment). One without a TroopComponent at all
    // can't be attributed to a client, so no indicator is shown for it.
    private bool OwnedByLocalPlayer(ulong entityId)
    {
        if (_troopStore == null || !_troopStore.HasComponent(entityId)) return false;
        return _troopStore.GetComponent(entityId).OwnerPlayerId == LocalPlayerId();
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
}
