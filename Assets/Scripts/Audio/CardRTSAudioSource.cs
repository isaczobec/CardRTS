using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A positioned audio emitter created via AudioManager.CreateAudioSource(...). Call
/// PlaySound(name) to start a clip; each call spawns its own underlying AudioSource so
/// multiple sounds can play concurrently from the same emitter, each independently
/// controllable through the returned PlayingSound. If linked to an entity with
/// followEntity enabled, AudioManager repositions this source every frame; AudioManager
/// also drives UpdateSounds() every frame to advance any crossfade loops.
/// </summary>
public class CardRTSAudioSource
{
    public ulong? EntityId { get; }
    public bool FollowEntity { get; }
    public float SpatialBlend { get; set; }
    public bool IsDestroyed { get; private set; }

    internal bool EntityDeleted { get; private set; }
    internal bool HasActiveSounds => _activeSounds.Count > 0;

    // Set via DestroyWhenIdle() for sources that don't need to persist once whatever they're
    // currently playing (including any in-flight fade-out) is done.
    internal bool AutoDestroyWhenIdle { get; private set; }

    private readonly GameObject _root;
    private readonly List<PlayingSound> _activeSounds = new List<PlayingSound>();
    private readonly float _minDistance;
    private readonly float _maxDistance;

    internal CardRTSAudioSource(GameObject root, ulong? entityId, bool followEntity, float spatialBlend, float minDistance, float maxDistance)
    {
        _root = root;
        EntityId = entityId;
        FollowEntity = followEntity;
        SpatialBlend = spatialBlend;
        _minDistance = minDistance;
        _maxDistance = maxDistance;
    }

    /// <param name="volume">
    /// Multiplied by the sound's SoundRegistry entry.defaultVolume to get the final playback
    /// volume — use the registry's defaultVolume to fix a clip that's balanced too quiet/loud
    /// across every call site, and this argument for one-off per-call adjustment on top of that.
    /// </param>
    /// <param name="loop">If true, the clip repeats indefinitely until PlayingSound.Stop() is called.</param>
    /// <param name="crossfadeDuration">
    /// Only meaningful when loop is true. If greater than zero, the tail of each cycle
    /// crossfades into a fresh restart over this many seconds instead of looping natively,
    /// hiding an imperfect loop point. Clamped to at most half the clip's length.
    /// </param>
    public PlayingSound PlaySound(string soundName, float volume = 1f, bool loop = false, float crossfadeDuration = 0f)
    {
        if (IsDestroyed) return null;

        if (SoundRegistry.instance == null || !SoundRegistry.instance.TryGet(soundName, out SoundRegistryEntry entry))
        {
            // Debug.LogWarning($"CardRTSAudioSource: no sound registered for key '{soundName}'.");
            return null;
        }

        AudioClip clip = entry.GetRandomClip();
        if (clip == null)
        {
            Debug.LogWarning($"CardRTSAudioSource: sound '{soundName}' has no clips assigned.");
            return null;
        }

        float pitch = entry.GetRandomPitch();
        // entry.defaultVolume compensates for a quiet clip at the registry level - deliberately
        // not clamped to 1 here so it can boost past the caller's own [0,1] volume if needed.
        float finalVolume = Mathf.Max(0f, volume) * entry.defaultVolume;
        float safeCrossfade = loop && crossfadeDuration > 0f ? Mathf.Min(crossfadeDuration, clip.length * 0.5f) : 0f;

        GameObject soundObject = new GameObject($"Sound_{soundName}");
        soundObject.transform.SetParent(_root.transform, false);

        AudioSource audioSource = soundObject.AddComponent<AudioSource>();
        audioSource.clip = clip;
        audioSource.volume = finalVolume;
        audioSource.pitch = pitch;
        audioSource.loop = loop && safeCrossfade <= 0f;
        audioSource.spatialBlend = SpatialBlend;
        audioSource.dopplerLevel = 0f;
        audioSource.minDistance = _minDistance;
        audioSource.maxDistance = _maxDistance;
        audioSource.Play();

        PlayingSound sound = new PlayingSound(_root.transform, clip, audioSource, soundObject, finalVolume, loop, safeCrossfade, SpatialBlend, pitch, _minDistance, _maxDistance);
        _activeSounds.Add(sound);
        return sound;
    }

    /// Manually repositions this source. Called by AudioManager each frame when FollowEntity is true.
    public void SetPosition(Vector3 position)
    {
        if (_root != null) _root.transform.position = position;
    }

    /// Called by AudioManager when this source's linked entity is deleted. Looping sounds have
    /// no natural end so they're stopped immediately; one-shots are left to finish naturally.
    /// AudioManager destroys this source once HasActiveSounds goes false.
    internal void NotifyEntityDeleted()
    {
        EntityDeleted = true;
        foreach (PlayingSound sound in _activeSounds)
        {
            if (sound.IsLooping) sound.Stop();
        }
    }

    internal void UpdateSounds(float deltaTime)
    {
        for (int i = _activeSounds.Count - 1; i >= 0; i--)
        {
            PlayingSound sound = _activeSounds[i];
            sound.Tick(deltaTime);
            if (sound.IsFinished)
            {
                sound.DestroyInternal();
                _activeSounds.RemoveAt(i);
            }
        }
    }

    /// Marks this source for automatic destruction once it has no active sounds left (including
    /// any still fading out) — use instead of Destroy() when in-flight sounds/fades should
    /// finish naturally rather than being cut off immediately.
    public void DestroyWhenIdle()
    {
        if (IsDestroyed) return;
        AutoDestroyWhenIdle = true;
    }

    /// Stops every sound playing on this source and tears down its GameObject.
    public void Destroy()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;

        foreach (PlayingSound sound in _activeSounds)
            sound.DestroyInternal();
        _activeSounds.Clear();

        if (_root != null) Object.Destroy(_root);
    }
}
