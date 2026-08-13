using System.Collections.Generic;
using UnityEngine;

// Shows a flat ground disc under a troop while it's channeling a manual recall back to its
// owner's deck — from the moment RecallSystem adds a RecallingComponent to it until the
// channel either completes (entity deleted) or is cancelled early (component removed) — see
// RecallSystem/RecallingComponent for the channel itself. Mirrors
// DeployProgressIndicatorManager exactly (see that class's own doc comment), just keyed off
// RecallingComponent instead of ActivatableComponent, and without a modifier-following case
// (a RecallingComponent is only ever added to a troop/building entity that already has its
// own PositionComponent, never to a standalone modifier entity). Unlike
// DeployProgressIndicatorManager, this is shown for EVERY client, not just the one whose troop
// is recalling — a recall is a meaningful, actionable event for the opponent too (the troop is
// briefly helpless and about to vanish), so every viewer should see it counting down.
//
// A recalling entity can't move (RecallSystem vetoes CanMoveRequest/
// CanMoveOnOwnAccountRequest for it), so like DeployProgressIndicatorManager's own troop
// case, each indicator's position is only ever set once, at creation.
//
// Separate from the ECS architecture, like DeployProgressIndicatorManager/
// CardRangeIndicatorManager — never registered as an ISystem, just polls the live ECS each
// frame to update each indicator's fill.
public class RecallProgressIndicatorManager : Singleton<RecallProgressIndicatorManager>
{
    [SerializeField] private DeployProgressIndicatorPrefab _indicatorPrefab;

    private ECS _ecs;
    private ComponentStore<RecallingComponent> _recallStore;
    private ComponentStore<PositionComponent> _positionStore;

    private readonly Dictionary<ulong, DeployProgressIndicatorPrefab> _indicators = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentAddedEvent<RecallingComponent>>(OnRecallingAdded);
        TickManager.instance.ServerFlagEvents.Subscribe<ComponentRemovedEvent<RecallingComponent>>(OnRecallingRemoved);
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);

        _ecs = TickManager.instance.ActiveECS;
        _recallStore = _ecs.GetComponentStore<RecallingComponent>();
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        foreach (KeyValuePair<ulong, DeployProgressIndicatorPrefab> kvp in _indicators)
        {
            ulong entityId = kvp.Key;
            DeployProgressIndicatorPrefab indicator = kvp.Value;

            if (!_recallStore.HasComponent(entityId))
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            RecallingComponent recall = _recallStore.GetComponent(entityId);
            if (recall.InitialTicksRemaining == 0)
            {
                indicator.gameObject.SetActive(false);
                continue;
            }

            indicator.gameObject.SetActive(true);
            indicator.SetProgress((float)recall.TicksRemaining / recall.InitialTicksRemaining);
        }
    }

    // ── Indicator lifecycle — one per recalling entity, for every client. ────────────────

    private void OnRecallingAdded(ComponentAddedEvent<RecallingComponent> e)
    {
        ulong entityId = e.EntityId;
        if (_indicators.ContainsKey(entityId)) return;
        if (_recallStore == null || !_recallStore.HasComponent(entityId)) return;

        RecallingComponent recall = _recallStore.GetComponent(entityId);
        if (recall.InitialTicksRemaining == 0) return; // instant recall — nothing to show
        if (_positionStore == null || !_positionStore.HasComponent(entityId)) return;
        if (_indicatorPrefab == null) return;

        PositionComponent pos = _positionStore.GetComponent(entityId);
        DeployProgressIndicatorPrefab indicator = Instantiate(_indicatorPrefab, WorldPositionFor(pos), Quaternion.identity, transform);
        indicator.name = $"RecallProgressIndicator_{entityId}";
        indicator.SetProgress(1f);

        _indicators[entityId] = indicator;
    }

    private void OnRecallingRemoved(ComponentRemovedEvent<RecallingComponent> e) => DestroyIndicator(e.EntityId);

    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyIndicator(e.EntityId);

    private void DestroyIndicator(ulong entityId)
    {
        if (_indicators.TryGetValue(entityId, out DeployProgressIndicatorPrefab indicator))
        {
            Destroy(indicator.gameObject);
            _indicators.Remove(entityId);
        }
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }
}
