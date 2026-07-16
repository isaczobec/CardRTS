using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central entry point for spawning audio sources. Call AudioManager.instance.CreateAudioSource(...)
/// to obtain a CardRTSAudioSource, then PlaySound(name) on it to start playback. Mirrors
/// HealthBarManager's entity-follow pattern: position lookups go through a cached
/// PositionComponent store, refreshed every Update(). Initialize() must be called once
/// TickManager.instance.ActiveECS exists (see RenderingSetup).
/// </summary>
public class AudioManager : Singleton<AudioManager>
{
    // Applied to every 3D (spatialBlend > 0) AudioSource this manager creates. minDistance
    // pushed out well past Unity's default of 1 so volume stays consistent for longer as
    // the camera pans away from an entity, instead of falling off almost immediately.
    [Header("3D Sound Falloff")]
    [SerializeField] private float _minDistance = 15f;
    [SerializeField] private float _maxDistance = 500f;

    private ComponentStore<PositionComponent> _positionStore;

    private readonly List<CardRTSAudioSource> _sources = new List<CardRTSAudioSource>();

    public void Initialize()
    {
        ECS ecs = TickManager.instance.ActiveECS;
        _positionStore = ecs.GetComponentStore<PositionComponent>();

        TickManager.instance.ServerFlagEvents.Subscribe<EntityDeletedEvent>(OnEntityDeleted);
    }

    /// Entity-linked sources aren't destroyed the instant their entity dies - a still-playing
    /// one-shot (e.g. a death sound) is allowed to finish first. See CardRTSAudioSource.NotifyEntityDeleted.
    private void OnEntityDeleted(EntityDeletedEvent e)
    {
        foreach (CardRTSAudioSource source in _sources)
        {
            if (!source.IsDestroyed && source.EntityId == e.EntityId)
                source.NotifyEntityDeleted();
        }
    }

    /// Creates a standalone audio source at a fixed world position, not linked to any entity.
    public CardRTSAudioSource CreateAudioSource(Vector3 position, float spatialBlend = 1f)
    {
        GameObject root = new GameObject("AudioSource");
        root.transform.SetParent(transform, false);
        root.transform.position = position;

        CardRTSAudioSource source = new CardRTSAudioSource(root, entityId: null, followEntity: false, spatialBlend, _minDistance, _maxDistance);
        _sources.Add(source);
        return source;
    }

    /// Creates an audio source linked to an entity. When followEntity is true (default),
    /// the source is repositioned every frame from the entity's PositionComponent.
    public CardRTSAudioSource CreateAudioSource(ulong entityId, float spatialBlend = 1f, bool followEntity = true)
    {
        GameObject root = new GameObject($"AudioSource_Entity{entityId}");
        root.transform.SetParent(transform, false);
        if (_positionStore != null && _positionStore.HasComponent(entityId))
            root.transform.position = GetWorldPosition(_positionStore.GetComponent(entityId));

        CardRTSAudioSource source = new CardRTSAudioSource(root, entityId, followEntity, spatialBlend, _minDistance, _maxDistance);
        _sources.Add(source);
        return source;
    }

    void Update()
    {
        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            CardRTSAudioSource source = _sources[i];
            if (source.IsDestroyed)
            {
                _sources.RemoveAt(i);
                continue;
            }

            if (source.FollowEntity && source.EntityId.HasValue && _positionStore != null
                && _positionStore.HasComponent(source.EntityId.Value))
            {
                source.SetPosition(GetWorldPosition(_positionStore.GetComponent(source.EntityId.Value)));
            }

            source.UpdateSounds(Time.deltaTime);

            if ((source.EntityDeleted || source.AutoDestroyWhenIdle) && !source.HasActiveSounds)
                source.Destroy();
        }
    }

    /// Plays a single one-shot sound at a fixed world position and cleans up automatically
    /// once playback finishes - for transient positional SFX (e.g. spawn/complete chimes)
    /// that don't need a persistent emitter of their own. Returns null if the sound key
    /// wasn't found in SoundRegistry.
    public PlayingSound PlayOneShotAtPosition(string soundName, Vector3 position, float volume = 1f, float spatialBlend = 1f)
    {
        CardRTSAudioSource source = CreateAudioSource(position, spatialBlend);
        PlayingSound sound = source.PlaySound(soundName, volume);
        if (sound == null)
        {
            source.Destroy();
            return null;
        }

        source.DestroyWhenIdle();
        return sound;
    }

    private static Vector3 GetWorldPosition(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height, pos.Y);
    }
}
