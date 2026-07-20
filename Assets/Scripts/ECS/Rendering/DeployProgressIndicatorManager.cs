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
// Also handles a second, related case: a modifier entity (ModifierComponent — see
// ModifierSystem) that also carries an ActivatableComponent (e.g. SpeedBoostCard's buff,
// which has a short deploy delay before it actually takes effect). A modifier has no
// PositionComponent of its own, so its progress indicator instead follows
// ModifierComponent.TargetEntityId's position every frame (unlike every other indicator
// here, which is only ever positioned once, at creation) — the entity being buffed could
// be actively moving while the buff is still deploying. Visibility there is gated on the
// TARGET's ownership, not the modifier's own (it has none) — see
// OnModifierActivatableAdded.
//
// Separate from the ECS architecture, like CardRangeIndicatorManager — never registered as
// an ISystem, just polls the live ECS each frame to update each indicator's fill. Deploying
// troops/buildings never move (TroopCardHelper/BuildingSpawnHelper both leave a fresh
// entity's destination equal to its spawn position, and ActivationQuery.IsActivated —
// which gates PathfindingSystem's player-order handling — is false until activation
// finishes), so like CardRangeIndicatorManager's building rings, each of THOSE indicators'
// position is only ever set once, at creation.
public class DeployProgressIndicatorManager : Singleton<DeployProgressIndicatorManager>
{
    [SerializeField] private DeployProgressIndicatorPrefab _indicatorPrefab;

    private ECS _ecs;
    private ComponentStore<ActivatableComponent> _activatableStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<ModifierComponent> _modifierStore;

    private readonly Dictionary<ulong, DeployProgressIndicatorPrefab> _indicators = new();
    // Keyed by modifier entity id (not the target it follows) — see OnModifierActivatableAdded.
    private readonly Dictionary<ulong, DeployProgressIndicatorPrefab> _modifierIndicators = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<ActivatableComponent>>(OnActivatableAdded);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityActivatedEvent>(OnEntityActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);

        _ecs = TickManager.instance.ActiveECS;
        _activatableStore = _ecs.GetComponentStore<ActivatableComponent>();
        _troopStore = _ecs.GetComponentStore<TroopComponent>();
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _modifierStore = _ecs.GetComponentStore<ModifierComponent>();
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

        foreach (KeyValuePair<ulong, DeployProgressIndicatorPrefab> kvp in _modifierIndicators)
        {
            ulong modifierId = kvp.Key;
            DeployProgressIndicatorPrefab indicator = kvp.Value;

            if (!_activatableStore.HasComponent(modifierId) || !_modifierStore.HasComponent(modifierId))
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            ActivatableComponent activatable = _activatableStore.GetComponent(modifierId);
            if (activatable.InitialTicksUntilActive == 0)
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            ulong targetId = _modifierStore.GetComponent(modifierId).TargetEntityId;
            if (!_positionStore.HasComponent(targetId))
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            // Unlike the loop above, re-read the target's position every frame — it's a
            // separate, potentially-moving entity, not the modifier itself.
            indicator.transform.position = WorldPositionFor(_positionStore.GetComponent(targetId));
            indicator.gameObject.SetActive(true);
            indicator.SetProgress((float)activatable._ticksUntilActive / activatable.InitialTicksUntilActive);
        }
    }

    // ── Indicator lifecycle — one per deploying entity/modifier relevant to the local
    // client. ────────────────────────────────────────────────────────────────────────

    private void OnActivatableAdded(ComponentAddedEvent<ActivatableComponent> e)
    {
        if (_modifierStore != null && _modifierStore.HasComponent(e.EntityId))
        {
            OnModifierActivatableAdded(e.EntityId);
            return;
        }

        OnSpawnedEntityActivatableAdded(e.EntityId);
    }

    private void OnSpawnedEntityActivatableAdded(ulong entityId)
    {
        if (_indicators.ContainsKey(entityId)) return;
        if (_activatableStore == null || !_activatableStore.HasComponent(entityId)) return;
        if (!OwnedByLocalPlayer(entityId)) return;

        ActivatableComponent activatable = _activatableStore.GetComponent(entityId);
        if (activatable.InitialTicksUntilActive == 0) return; // no deploy delay — nothing to show
        if (_positionStore == null || !_positionStore.HasComponent(entityId)) return;
        if (_indicatorPrefab == null) return;

        PositionComponent pos = _positionStore.GetComponent(entityId);
        DeployProgressIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(pos), Quaternion.identity, transform);
        indicator.name = $"DeployProgressIndicator_{entityId}";
        indicator.SetProgress(1f);

        _indicators[entityId] = indicator;

        AudioManager.instance?.PlayOneShotAtPosition("DeploySpawn", indicator.transform.position);
    }

    // A modifier entity (ModifierComponent + ActivatableComponent) shows its progress
    // indicator FOLLOWING the entity it targets, not itself (a modifier has no
    // PositionComponent of its own) — see Update, which repositions these every frame
    // since the target could be moving. Visibility is gated on the TARGET's ownership (via
    // _troopStore, the same store used for OwnedByLocalPlayer above) rather than the
    // modifier's own — it has no TroopComponent/owner of its own.
    private void OnModifierActivatableAdded(ulong modifierId)
    {
        if (_modifierIndicators.ContainsKey(modifierId)) return;
        if (_activatableStore == null || !_activatableStore.HasComponent(modifierId)) return;

        ActivatableComponent activatable = _activatableStore.GetComponent(modifierId);
        if (activatable.InitialTicksUntilActive == 0) return; // no deploy delay — nothing to show

        ulong targetId = _modifierStore.GetComponent(modifierId).TargetEntityId;
        if (_positionStore == null || !_positionStore.HasComponent(targetId)) return;
        if (_troopStore == null || !_troopStore.HasComponent(targetId)) return;
        if (_troopStore.GetComponent(targetId).OwnerPlayerId != LocalPlayerId()) return;
        if (_indicatorPrefab == null) return;

        PositionComponent targetPos = _positionStore.GetComponent(targetId);
        DeployProgressIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(targetPos), Quaternion.identity, transform);
        indicator.name = $"DeployProgressIndicator_Modifier_{modifierId}";
        indicator.SetProgress(1f);

        _modifierIndicators[modifierId] = indicator;

        AudioManager.instance?.PlayOneShotAtPosition("DeploySpawn", indicator.transform.position);
    }

    // Only the successful-completion path (not early removal via OnEntityDeleted) plays a
    // sound — position is read off the indicator itself before DestroyIndicator tears it down.
    private void OnEntityActivated(EntityActivatedEvent e)
    {
        Vector3? indicatorPosition = GetIndicatorPosition(e.EntityId);
        DestroyIndicator(e.EntityId);

        if (indicatorPosition.HasValue)
            AudioManager.instance?.PlayOneShotAtPosition("DeployComplete", indicatorPosition.Value);
    }

    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyIndicator(e.EntityId);

    private Vector3? GetIndicatorPosition(ulong entityId)
    {
        if (_indicators.TryGetValue(entityId, out DeployProgressIndicatorPrefab indicator))
            return indicator.transform.position;
        if (_modifierIndicators.TryGetValue(entityId, out DeployProgressIndicatorPrefab modifierIndicator))
            return modifierIndicator.transform.position;
        return null;
    }

    private void DestroyIndicator(ulong entityId)
    {
        if (_indicators.TryGetValue(entityId, out DeployProgressIndicatorPrefab indicator))
        {
            Destroy(indicator.gameObject);
            _indicators.Remove(entityId);
        }

        if (_modifierIndicators.TryGetValue(entityId, out DeployProgressIndicatorPrefab modifierIndicator))
        {
            Destroy(modifierIndicator.gameObject);
            _modifierIndicators.Remove(entityId);
        }
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
