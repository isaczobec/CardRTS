using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic IModifierRenderer implementation: plays _soundName (looping by default) on an
/// AudioManager source linked to the target entity once a modifier of this renderer's type
/// activates on it, and explicitly stops that sound (PlayingSound.Stop()) once the modifier
/// is gone — the modifier's own duration ends the sound, not the clip finishing on its own.
/// One instance handles one RenderableModifierType — register it against that type via
/// RenderableModifierManager's Inspector list, the same way e.g.
/// OverlayMaterialModifierRenderer/PrefabModifierRenderer are.
/// </summary>
public class SoundModifierRenderer : MonoBehaviour, IModifierRenderer
{
    [SerializeField] private string _soundName;
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;
    // Looping by default — this sound is meant to persist for as long as the modifier is
    // active and be explicitly stopped, unlike a one-shot that just finishes on its own.
    // Set false for a modifier whose activation cue is a short one-shot with nothing to stop.
    [SerializeField] private bool _loop = true;
    // Only meaningful while _loop is true — see CardRTSAudioSource.PlaySound.
    [SerializeField] private float _crossfadeDuration = 0f;

    // Optional one-shot played at the target's last known position when the modifier ends —
    // e.g. a "shatter" cue distinct from the sustained loop above. Left blank to play nothing.
    [SerializeField] private string _endSoundName;
    [SerializeField, Range(0f, 1f)] private float _endSoundVolume = 1f;

    private ComponentStore<PositionComponent> _positionStore;
    private readonly Dictionary<ulong, CardRTSAudioSource> _sources = new();
    private readonly Dictionary<ulong, PlayingSound> _sounds = new();

    public void Initialize(ECS ecs)
    {
        _positionStore = ecs.GetComponentStore<PositionComponent>();
    }

    // Deliberately a no-op — the sound starts on OnEntityActivated instead, same
    // deploy-delay convention every other renderer in this project follows.
    public void OnEntityAdded(ulong targetEntityId) { }

    public void OnEntityRemoved(ulong targetEntityId)
    {
        StopSound(targetEntityId);
        PlayEndSound(targetEntityId);
    }

    // Can fire more than once for the same target (see IModifierRenderer) — guarded the
    // same way every other renderer's OnEntityActivated already guards against re-adding.
    public void OnEntityActivated(ulong targetEntityId)
    {
        if (_sources.ContainsKey(targetEntityId) || AudioManager.instance == null) return;

        CardRTSAudioSource source = AudioManager.instance.CreateAudioSource(targetEntityId);
        PlayingSound sound = source.PlaySound(_soundName, _volume, _loop, _crossfadeDuration);
        if (sound == null)
        {
            source.Destroy();
            return;
        }

        _sources[targetEntityId] = source;
        _sounds[targetEntityId] = sound;
    }

    public void UpdateRenderable(List<ulong> targetEntityIds) { }

    private void StopSound(ulong targetEntityId)
    {
        if (_sounds.TryGetValue(targetEntityId, out PlayingSound sound))
            sound.Stop();
        _sounds.Remove(targetEntityId);

        if (_sources.TryGetValue(targetEntityId, out CardRTSAudioSource source))
            source.DestroyWhenIdle();
        _sources.Remove(targetEntityId);
    }

    // No-op (rather than falling back to some default position) if the target's own position
    // is no longer available — e.g. the modifier is being removed because the target itself
    // just died and was already deleted from the ECS.
    private void PlayEndSound(ulong targetEntityId)
    {
        if (string.IsNullOrEmpty(_endSoundName) || AudioManager.instance == null) return;
        if (_positionStore == null || !_positionStore.HasComponent(targetEntityId)) return;

        AudioManager.instance.PlayOneShotAtPosition(_endSoundName, WorldPositionFor(_positionStore.GetComponent(targetEntityId)), _endSoundVolume);
    }

    private static Vector3 WorldPositionFor(PositionComponent pos)
    {
        float height = WorldManager.instance.Handler.GetHeight(pos.TileX, pos.TileY);
        return new Vector3(pos.X, height, pos.Y);
    }
}
