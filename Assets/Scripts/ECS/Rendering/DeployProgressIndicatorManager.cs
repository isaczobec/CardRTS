using System.Collections.Generic;
using UnityEngine;

// Shows a flat ground disc under a troop/building while it's mid-deploy — from the moment
// a SpawnAtPointCard spawns it (TroopComponent is added to every entity a SpawnAtPointCard
// creates, troop or building alike — see TroopCardHelper/BuildingSpawnHelper) until its
// activation delay finishes (TroopActivatedEvent) or it's removed early (EntityDeletedEvent).
// Visible only for the client whose card spawned it — this is player-facing UI, not
// server-confirmed game state other players need to see.
//
// Separate from the ECS architecture, like CardRangeIndicatorManager — never registered as
// an ISystem, just polls the live ECS each frame to update each indicator's fill. Deploying
// entities never move (TroopCardHelper/BuildingSpawnHelper both leave a fresh entity's
// destination equal to its spawn position, and CanTakeActions — which gates
// PathfindingSystem's player-order handling — is false until activation finishes), so like
// CardRangeIndicatorManager's building rings, each indicator's position is only ever set
// once, at creation.
public class DeployProgressIndicatorManager : Singleton<DeployProgressIndicatorManager>
{
    [SerializeField] private DeployProgressIndicatorPrefab _indicatorPrefab;

    private ECS _ecs;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<PositionComponent> _positionStore;

    private readonly Dictionary<ulong, DeployProgressIndicatorPrefab> _indicators = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<TroopComponent>>(OnTroopAdded);
        TickManager.instance.ServerFlagEvents.Subscribe<TroopActivatedEvent>(OnTroopActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);

        _ecs = TickManager.instance.ActiveECS;
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

            if (!_troopStore.HasComponent(entityId))
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            TroopComponent troop = _troopStore.GetComponent(entityId);
            if (troop.InitialTicksUntilActive == 0)
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            indicator.gameObject.SetActive(true);
            indicator.SetProgress((float)troop._ticksUntilActive / troop.InitialTicksUntilActive);
        }
    }

    // ── Indicator lifecycle — one per deploying entity owned by the local client. ──────

    private void OnTroopAdded(ComponentAddedEvent<TroopComponent> e)
    {
        if (_indicators.ContainsKey(e.EntityId)) return;
        if (_troopStore == null || !_troopStore.HasComponent(e.EntityId)) return;

        TroopComponent troop = _troopStore.GetComponent(e.EntityId);
        if (troop.OwnerPlayerId != LocalPlayerId()) return;
        if (troop.InitialTicksUntilActive == 0) return; // no deploy delay — nothing to show
        if (_positionStore == null || !_positionStore.HasComponent(e.EntityId)) return;
        if (_indicatorPrefab == null) return;

        PositionComponent pos = _positionStore.GetComponent(e.EntityId);
        DeployProgressIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(pos), Quaternion.identity, transform);
        indicator.name = $"DeployProgressIndicator_{e.EntityId}";
        indicator.SetProgress(1f);

        _indicators[e.EntityId] = indicator;
    }

    private void OnTroopActivated(TroopActivatedEvent e) => DestroyIndicator(e.EntityId);
    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyIndicator(e.EntityId);

    private void DestroyIndicator(ulong entityId)
    {
        if (!_indicators.TryGetValue(entityId, out DeployProgressIndicatorPrefab indicator)) return;
        Destroy(indicator.gameObject);
        _indicators.Remove(entityId);
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;
}
