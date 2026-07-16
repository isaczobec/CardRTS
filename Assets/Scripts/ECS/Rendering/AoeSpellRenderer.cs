using System.Collections.Generic;
using UnityEngine;

// Renders active AoeSpellCard entities (DamageAuraComponent + LifetimeComponent): a flat
// ground disc instantiated at 1-unit diameter and scaled up to the entity's Range stat,
// with a per-instance material continuously fed the entity's elapsed-lifetime fraction
// (AoeSpellPrefab.SetDurationElapsed) so the effect can animate itself out over its
// duration. Register an instance with RenderableManager for RenderableType.AoeSpell.
//
// Stationary — an AOE spell entity has no MovableComponent — so unlike BasicTroopRenderer's
// TickPositionInterpolator, position/scale are only ever set once, at activation.
//
// Also owns the spell's audio: a crossfading looping "active" sound starts alongside the
// visual on activation and fades out (rather than cutting off) when the spell ends, and a
// one-shot "damage" sound plays on every DamageDealtEvent this spell's own entity deals
// (DealerEntityId lookup against _audioSources, so other damage sources don't trigger it).
public class AoeSpellRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private AoeSpellPrefab _prefab;

    [Header("Audio")]
    [SerializeField] private float _loopCrossfadeDuration = 1.5f;
    [SerializeField] private float _stopFadeOutDuration = 1f;

    [SerializeField] private string _loopSoundName;
    [SerializeField] private string _damageSoundName;

    private const int DefaultRange = 5;

    private ECS _ecs;
    private readonly Dictionary<ulong, AoeSpellPrefab> _objects = new();
    private readonly Dictionary<ulong, CardRTSAudioSource> _audioSources = new();
    private readonly Dictionary<ulong, PlayingSound> _loopSounds = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    public void OnEntityAdded(ulong entityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same as every
        // other renderer, so a still-deploying spell stays invisible until it's active.
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out AoeSpellPrefab go))
            Destroy(go.gameObject);
        _objects.Remove(entityId);

        if (_loopSounds.TryGetValue(entityId, out PlayingSound loopSound))
        {
            loopSound.Stop(_stopFadeOutDuration);
            _loopSounds.Remove(entityId);
        }

        if (_audioSources.TryGetValue(entityId, out CardRTSAudioSource audioSource))
        {
            audioSource.DestroyWhenIdle(); // lets the fade-out above finish before tearing down
            _audioSources.Remove(entityId);
        }
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _prefab == null) return;

        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return;

        PositionComponent pos = posStore.GetComponent(entityId);
        Vector3 worldPos = WorldPositionFor(pos);

        AoeSpellPrefab go = Instantiate(_prefab, worldPos, Quaternion.identity);
        go.name = $"AoeSpell_{entityId}";

        float range = StatsQuery.GetRange(_ecs, entityId, DefaultRange);
        go.SetScale(range * 2f); // radius -> diameter
        go.SetDurationElapsed(0f);

        _objects[entityId] = go;

        if (AudioManager.instance != null)
        {
            CardRTSAudioSource audioSource = AudioManager.instance.CreateAudioSource(worldPos, spatialBlend: 1f);
            _audioSources[entityId] = audioSource;
            _loopSounds[entityId] = audioSource.PlaySound(_loopSoundName, loop: true, crossfadeDuration: _loopCrossfadeDuration);
        }
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_audioSources.TryGetValue(e.DealerEntityId, out CardRTSAudioSource audioSource))
            audioSource.PlaySound(_damageSoundName);
    }

    public void UpdateRenderable(List<ulong> entityIds)
    {
        var lifetimeStore = _ecs?.GetComponentStore<LifetimeComponent>();
        if (lifetimeStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_objects.TryGetValue(id, out AoeSpellPrefab go)) continue;
            if (!lifetimeStore.HasComponent(id)) continue;

            LifetimeComponent lifetime = lifetimeStore.GetComponent(id);
            float elapsed = lifetime.InitialTicksRemaining > 0
                ? 1f - (float)lifetime.TicksRemaining / lifetime.InitialTicksRemaining
                : 0f;
            go.SetDurationElapsed(Mathf.Clamp01(elapsed));
        }
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height + 0.01f, pos.Y);
    }
}
