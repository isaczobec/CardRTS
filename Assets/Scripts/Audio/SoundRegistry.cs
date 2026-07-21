using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class OverlaySoundEntry
{
    public AudioClip clip;

    [Tooltip("Chance [0,1] this overlay plays alongside the main clip. See SoundRegistryEntry.RollOverlayClip for exactly how this is rolled.")]
    [Range(0f, 1f)] public float probability = 0f;

    [Tooltip("Baseline volume multiplier for this overlay clip specifically - independent of the main entry's defaultVolume, since an extra layered clip (e.g. a bonus crunch/impact layer) often needs its own balancing.")]
    public float volume = 1f;
}

[System.Serializable]
public class SoundRegistryEntry
{
    public string name;

    /// One clip is picked at random each time PlaySound() looks this entry up.
    public List<AudioClip> clips = new List<AudioClip>();

    [Tooltip("Playback pitch is randomized uniformly within this range each time the sound is played. Leave both at 1 for no randomization.")]
    public float minPitch = 1f;
    public float maxPitch = 1f;

    [Tooltip("Baseline volume multiplier applied on top of the volume argument passed to PlaySound() - use this to fix a clip that's balanced too quiet (or loud) across every call site. Not capped at 1, so it can boost a quiet clip.")]
    public float defaultVolume = 1f;

    [Tooltip("Extra clips layered on top of the main clip on some plays - see RollOverlayClip.")]
    public List<OverlaySoundEntry> overlaySounds = new List<OverlaySoundEntry>();

    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Count == 0) return null;
        return clips[Random.Range(0, clips.Count)];
    }

    public float GetRandomPitch() => Random.Range(minPitch, maxPitch);

    /// Rolls a single cumulative-probability table across overlaySounds (in list order) and
    /// returns the chosen clip, or null if the roll lands past the end of the table (or
    /// there are no overlay entries at all) - so at most one overlay is ever chosen per
    /// call, never more than one. If the probabilities across the whole list sum to 1 or
    /// more, every roll lands inside some entry's slice, so one overlay is guaranteed to
    /// play; anything less than 1 leaves a gap that's the chance no overlay plays that time.
    public AudioClip RollOverlayClip(out float volume)
    {
        volume = 1f;
        if (overlaySounds == null || overlaySounds.Count == 0) return null;

        float roll = Random.value;
        float cumulative = 0f;
        foreach (OverlaySoundEntry overlay in overlaySounds)
        {
            if (overlay.clip == null) continue;
            cumulative += overlay.probability;
            if (roll < cumulative)
            {
                volume = overlay.volume;
                return overlay.clip;
            }
        }
        return null;
    }
}

/// <summary>
/// String -> sound definition lookup used by CardRTSAudioSource.PlaySound(name). Populate
/// _entries in the Inspector; one entry per sound key that PlaySound() is called with, each
/// with one or more clip variations and an optional pitch randomization range. Mirrors
/// ImageRegistry's shape.
/// </summary>
public class SoundRegistry : Singleton<SoundRegistry>
{
    [SerializeField] private List<SoundRegistryEntry> _entries = new List<SoundRegistryEntry>();

    private Dictionary<string, SoundRegistryEntry> _lookup;

    protected override void Awake()
    {
        base.Awake();

        _lookup = new Dictionary<string, SoundRegistryEntry>();
        foreach (SoundRegistryEntry entry in _entries)
        {
            if (string.IsNullOrEmpty(entry.name) || entry.clips == null || entry.clips.Count == 0) continue;
            _lookup[entry.name] = entry;
        }
    }

    public bool TryGet(string name, out SoundRegistryEntry entry)
    {
        if (_lookup == null || string.IsNullOrEmpty(name))
        {
            entry = null;
            return false;
        }
        return _lookup.TryGetValue(name, out entry);
    }
}
