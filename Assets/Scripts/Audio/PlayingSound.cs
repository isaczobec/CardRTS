using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Handle to a single clip started via CardRTSAudioSource.PlaySound(). Lets call sites
/// adjust volume or cancel playback early without touching Unity objects directly. When
/// created with a crossfade duration, silently manages a second overlapping voice so an
/// indefinitely-looping clip fades into its own restart instead of popping at the seam.
/// </summary>
public class PlayingSound
{
    private readonly Transform _parent;
    private readonly AudioClip _clip;
    private readonly bool _loop;
    private readonly float _crossfadeDuration;
    private readonly float _spatialBlend;
    private readonly float _pitch;
    private readonly float _minDistance;
    private readonly float _maxDistance;
    private readonly AudioMixerGroup _mixerGroup;
    private float _volume;
    private bool _stopped;

    private AudioSource _current;
    private GameObject _currentGO;
    private AudioSource _next;
    private GameObject _nextGO;
    private bool _crossfading;
    private float _crossfadeElapsed;

    private bool _fadingOut;
    private float _fadeOutDuration;
    private float _fadeOutElapsed;
    private float _fadeOutStartCurrentVolume;
    private float _fadeOutStartNextVolume;

    internal PlayingSound(Transform parent, AudioClip clip, AudioSource initialSource, GameObject initialGO,
        float volume, bool loop, float crossfadeDuration, float spatialBlend, float pitch, float minDistance, float maxDistance,
        AudioMixerGroup mixerGroup)
    {
        _parent = parent;
        _clip = clip;
        _volume = Mathf.Max(0f, volume);
        _loop = loop;
        _crossfadeDuration = crossfadeDuration;
        _spatialBlend = spatialBlend;
        _pitch = pitch;
        _minDistance = minDistance;
        _maxDistance = maxDistance;
        _mixerGroup = mixerGroup;

        _current = initialSource;
        _currentGO = initialGO;
    }

    public bool IsPlaying => !_stopped && ((_current != null && _current.isPlaying) || (_next != null && _next.isPlaying));

    internal bool IsLooping => _loop;

    /// True once a non-looping clip has finished playing, or after a Stop() fade (if any) completes.
    internal bool IsFinished => _stopped || (_current == null && _next == null)
        || (!_loop && !_fadingOut && _current != null && !_current.isPlaying);

    public void SetVolume(float volume)
    {
        _volume = Mathf.Max(0f, volume);
        if (!_crossfading && !_fadingOut && _current != null) _current.volume = _volume;
    }

    /// Cancels playback. With fadeDuration > 0, ramps volume down to zero over that many
    /// seconds (from whatever's currently playing, including mid-crossfade) before actually
    /// stopping; a fadeDuration of 0 (default) cuts playback immediately, same as before, and
    /// also cuts short any fade-out already in progress.
    public void Stop(float fadeDuration = 0f)
    {
        if (_stopped) return;

        if (fadeDuration <= 0f)
        {
            _stopped = true;
            _fadingOut = false;
            _current?.Stop();
            _next?.Stop();
            return;
        }

        if (_fadingOut) return; // already fading out over some duration — let it finish naturally

        _fadingOut = true;
        _fadeOutDuration = fadeDuration;
        _fadeOutElapsed = 0f;
        _fadeOutStartCurrentVolume = _current != null ? _current.volume : 0f;
        _fadeOutStartNextVolume = _next != null ? _next.volume : 0f;
    }

    private bool CrossfadeEnabled => _loop && _crossfadeDuration > 0f;

    internal void Tick(float deltaTime)
    {
        if (_stopped || _current == null) return;

        if (_fadingOut)
        {
            TickFadeOut(deltaTime);
            return;
        }

        if (!CrossfadeEnabled) return;

        if (!_crossfading)
        {
            // AudioSource.time is in clip-time, not wall-clock time, so scale by pitch to
            // get the actual seconds remaining before this voice reaches the clip's end.
            float timeRemaining = (_current.clip.length - _current.time) / Mathf.Max(Mathf.Abs(_pitch), 0.0001f);
            if (timeRemaining <= _crossfadeDuration)
                StartCrossfade();
            return;
        }

        _crossfadeElapsed += deltaTime;
        float t = Mathf.Clamp01(_crossfadeElapsed / _crossfadeDuration);
        if (_current != null) _current.volume = Mathf.Lerp(_volume, 0f, t);
        if (_next != null) _next.volume = Mathf.Lerp(0f, _volume, t);

        if (t >= 1f) CompleteCrossfade();
    }

    private void TickFadeOut(float deltaTime)
    {
        _fadeOutElapsed += deltaTime;
        float t = Mathf.Clamp01(_fadeOutElapsed / _fadeOutDuration);

        if (_current != null) _current.volume = Mathf.Lerp(_fadeOutStartCurrentVolume, 0f, t);
        if (_next != null) _next.volume = Mathf.Lerp(_fadeOutStartNextVolume, 0f, t);

        if (t >= 1f)
        {
            _current?.Stop();
            _next?.Stop();
            _stopped = true;
        }
    }

    private void StartCrossfade()
    {
        GameObject go = new GameObject($"Sound_{_clip.name}_Crossfade");
        go.transform.SetParent(_parent, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.clip = _clip;
        source.volume = 0f;
        source.pitch = _pitch;
        source.loop = false;
        source.spatialBlend = _spatialBlend;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = _minDistance;
        source.maxDistance = _maxDistance;
        source.outputAudioMixerGroup = _mixerGroup;
        source.Play();

        _next = source;
        _nextGO = go;
        _crossfading = true;
        _crossfadeElapsed = 0f;
    }

    private void CompleteCrossfade()
    {
        if (_currentGO != null) Object.Destroy(_currentGO);

        _current = _next;
        _currentGO = _nextGO;
        _current.volume = _volume;

        _next = null;
        _nextGO = null;
        _crossfading = false;
    }

    internal void DestroyInternal()
    {
        if (_currentGO != null) Object.Destroy(_currentGO);
        if (_nextGO != null) Object.Destroy(_nextGO);
        _current = null;
        _next = null;
    }
}
