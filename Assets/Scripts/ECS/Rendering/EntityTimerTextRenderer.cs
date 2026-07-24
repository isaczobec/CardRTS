using System.Collections.Generic;
using UnityEngine;

// Spawns a floating countdown label above any entity carrying a RespawnableInPlaceComponent
// (visible only while it's actually on cooldown, i.e. dead/respawning) or a LifetimeComponent
// (visible for the entity's whole life, since a lifetime always counts down from spawn) —
// gated in both cases by that component's own ShowTimer flag.
//
// Labels are created/destroyed on demand every frame by directly polling the two component
// stores, rather than tracked via activation events — RespawnableInPlaceComponent's "should I
// be visible" toggles on and off repeatedly across an entity's life (dies -> shows ->
// respawns -> hides -> dies again -> shows...), so a simple per-frame poll is both simpler
// and more robust than trying to track that transition through events.
//
// Positions snap directly to PositionComponent every frame (no TickPositionInterpolator) —
// every current user of these two components (Tree/Rock/Ore/soulstone/gem nodes,
// AoeSpellCard's aura) is stationary, so interpolation would be needless complexity; a future
// moving user would just see a slightly steppy label, which is fine for a countdown.
public class EntityTimerTextRenderer : Singleton<EntityTimerTextRenderer>
{
    [SerializeField] private TimerTextPrefab _timerTextPrefab;

    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<RespawnableInPlaceComponent> _respawnStore;
    private ComponentStore<LifetimeComponent> _lifetimeStore;

    private readonly Dictionary<ulong, TimerTextPrefab> _labels = new();

    public void Initialize()
    {
        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);

        _ecs = TickManager.instance.ActiveECS;
        _positionStore = _ecs.GetComponentStore<PositionComponent>();
        _respawnStore = _ecs.GetComponentStore<RespawnableInPlaceComponent>();
        _lifetimeStore = _ecs.GetComponentStore<LifetimeComponent>();
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (_timerTextPrefab == null || _positionStore == null) return;

        if (_respawnStore != null)
            _respawnStore.ForEach((ulong id) => UpdateRespawnableLabel(id));

        if (_lifetimeStore != null)
            _lifetimeStore.ForEach((ulong id) => UpdateLifetimeLabel(id));
    }

    private void UpdateRespawnableLabel(ulong entityId)
    {
        RespawnableInPlaceComponent respawn = _respawnStore.GetComponent(entityId);
        ApplyLabel(entityId, respawn.ShowTimer && respawn.IsOnCooldown, respawn.TicksUntilRespawn);
    }

    private void UpdateLifetimeLabel(ulong entityId)
    {
        // An entity carrying both isn't a case any current spawner produces, but if it ever
        // happens, let the respawnable countdown (checked first, above) take priority while
        // it's actually on cooldown rather than both fighting over the same label.
        if (_respawnStore != null && _respawnStore.HasComponent(entityId) && _respawnStore.GetComponent(entityId).IsOnCooldown)
            return;

        LifetimeComponent lifetime = _lifetimeStore.GetComponent(entityId);

        // Mirrors LifetimeSystem.Execute's own gating: TicksRemaining doesn't actually start
        // counting down until the entity is past its deploy delay (ActivationQuery.IsActive)
        // — showing the label before that would just freeze at the full duration (e.g. a
        // misleading "0:20" for however long a multi-second deploy delay lasts), rather than
        // genuinely reflecting "the countdown hasn't started yet."
        bool showTimer = lifetime.ShowTimer && ActivationQuery.IsActive(_ecs, entityId);
        ApplyLabel(entityId, showTimer, (ulong)Mathf.Max(0, lifetime.TicksRemaining));
    }

    private void ApplyLabel(ulong entityId, bool shouldShow, ulong ticksRemaining)
    {
        if (!shouldShow || !_positionStore.HasComponent(entityId))
        {
            DestroyLabel(entityId);
            return;
        }

        if (!_labels.TryGetValue(entityId, out TimerTextPrefab label))
        {
            GameObject go = Instantiate(_timerTextPrefab.gameObject, transform);
            go.name = $"TimerText_{entityId}";
            label = go.GetComponent<TimerTextPrefab>();
            _labels[entityId] = label;
        }

        PositionComponent pos = _positionStore.GetComponent(entityId);
        label.transform.position = WorldPositionFor(pos) + label.WorldOffset;
        label.SetText(FormatTime(ticksRemaining));
    }

    private void DestroyLabel(ulong entityId)
    {
        if (!_labels.TryGetValue(entityId, out TimerTextPrefab label)) return;
        Destroy(label.gameObject);
        _labels.Remove(entityId);
    }

    private void OnEntityDeleted(EntityDeletedEvent e) => DestroyLabel(e.EntityId);

    private static string FormatTime(ulong ticksRemaining)
    {
        int totalSeconds = Mathf.CeilToInt(TickManager.TicksToSeconds(ticksRemaining));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:00}";
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height, pos.Y);
    }
}
