using System.Collections.Generic;
using UnityEngine;

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

    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Count == 0) return null;
        return clips[Random.Range(0, clips.Count)];
    }

    public float GetRandomPitch() => Random.Range(minPitch, maxPitch);
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
