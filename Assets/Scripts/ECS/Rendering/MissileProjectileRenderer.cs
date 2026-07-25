using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders BallisticProjectileComponent entities (see TurretAISystem.FireBallistic/
/// MissileSiloCard) — a cosmetic-only missile with no hitbox at all. Purely visual: the
/// entity's own PositionComponent never moves (it's fixed at the launch point), so every
/// frame this computes an arced position itself, lerping launch -> target by the SAME 0..1
/// elapsed-fraction AoeSpellRenderer already derives from LifetimeComponent
/// (TicksRemaining/InitialTicksRemaining) and adding a parabolic height offset on top.
///
/// Also spawns a ground indicator at the target position, scaled to
/// BallisticProjectileComponent.ImpactRadius, that exists for the missile's whole flight —
/// a"landing zone" telegraph — and owns three sounds: one-shot on launch, a looping flight
/// sound that tracks the missile's own (visual) position every frame, and a one-shot on
/// impact (played the instant the entity is removed, i.e. its flight/lifetime ended).
///
/// Register an instance with RenderableManager for RenderableType.Missile.
/// </summary>
public class MissileProjectileRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _missilePrefab;
    [SerializeField] private GameObject _targetIndicatorPrefab;

    [Header("Impact")]
    [SerializeField] private GameObject _impactEffectPrefab;
    // Seconds after spawning before the impact effect instance is destroyed. <= 0 means
    // indefinite — the spawned instance is left alone entirely (e.g. a prefab that manages
    // its own lifetime, like a one-shot particle system set to auto-destroy on finish) —
    // mirrors DamageImpactEffectManager's own DamagerPrefabEntry.destroyAfterSeconds.
    [SerializeField] private float _impactEffectDestroyAfterSeconds = 3f;

    [Header("Flight")]
    [SerializeField] private float _arcHeight = 6f;
    // Degrees/second the missile turns to face its own current flight direction — 0 or less
    // snaps instantly instead of easing.
    [SerializeField] private float _rotationDegreesPerSecond = 1080f;

    [Header("Audio")]
    [SerializeField] private string _launchSoundName;
    [SerializeField] private string _impactSoundName;
    [SerializeField] private string _flightLoopSoundName;
    [SerializeField] private float _flightLoopCrossfadeDuration = 0.2f;
    [SerializeField] private float _flightLoopStopFadeOutDuration = 0.3f;

    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<BallisticProjectileComponent> _ballisticStore;
    private ComponentStore<LifetimeComponent> _lifetimeStore;

    private readonly Dictionary<ulong, GameObject> _missiles = new();
    private readonly Dictionary<ulong, GameObject> _targetIndicators = new();
    private readonly Dictionary<ulong, CardRTSAudioSource> _audioSources = new();
    private readonly Dictionary<ulong, PlayingSound> _loopSounds = new();

    // Smooths the per-tick arc position (computed fresh from LifetimeComponent each
    // UpdateRenderable call — see below) across the frames between simulation ticks, exactly
    // the way every other moving renderer in this project (BasicTroopRenderer,
    // SeekingProjectileRenderer, ...) smooths a raw ECS PositionComponent — the fed-in value
    // only actually changes once per simulation tick either way, so this Just Works even
    // though the "position" here is computed rather than read straight off the entity.
    private readonly TickPositionInterpolator _interpolator = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _ballisticStore = ecs.GetComponentStore<BallisticProjectileComponent>();
        _lifetimeStore = ecs.GetComponentStore<LifetimeComponent>();
    }

    // A missile has no activation-delay concept (no ActivatableComponent — fired instantly,
    // not deployed) so all setup happens here rather than in OnEntityActivated, mirroring
    // SeekingProjectileRenderer's own reasoning for pooled projectiles.
    public void OnEntityAdded(ulong entityId)
    {
        if (_missiles.ContainsKey(entityId)) return;
        if (_positionStore == null || !_positionStore.HasComponent(entityId)) return;
        if (_ballisticStore == null || !_ballisticStore.HasComponent(entityId)) return;

        BallisticProjectileComponent ballistic = _ballisticStore.GetComponent(entityId);
        Vector3 launchPos = ToWorldPosition(_positionStore.GetComponent(entityId));
        Vector3 targetPos = ToWorldPosition(ballistic.TargetX, ballistic.TargetY);

        if (_missilePrefab != null)
        {
            GameObject missile = Instantiate(_missilePrefab, launchPos, Quaternion.identity);
            missile.name = $"Missile_{entityId}";
            _missiles[entityId] = missile;
        }

        if (_targetIndicatorPrefab != null)
        {
            GameObject indicator = Instantiate(_targetIndicatorPrefab, targetPos, Quaternion.identity);
            indicator.name = $"MissileTargetIndicator_{entityId}";
            // Indicator prefab assumed authored at 1-unit diameter, same convention as
            // AoeSpellPrefab/RangeIndicatorPrefab — radius -> diameter.
            indicator.transform.localScale = new Vector3(ballistic.ImpactRadius * 2f, 1f, ballistic.ImpactRadius * 2f);
            _targetIndicators[entityId] = indicator;
        }

        PlaySoundAt(_launchSoundName, launchPos);

        if (AudioManager.instance != null && !string.IsNullOrEmpty(_flightLoopSoundName))
        {
            CardRTSAudioSource audioSource = AudioManager.instance.CreateAudioSource(launchPos, spatialBlend: 1f);
            _audioSources[entityId] = audioSource;
            _loopSounds[entityId] = audioSource.PlaySound(_flightLoopSoundName, loop: true, crossfadeDuration: _flightLoopCrossfadeDuration);
        }
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_missiles.TryGetValue(entityId, out GameObject missile))
        {
            SpawnImpactEffect(missile.transform.position);
            PlaySoundAt(_impactSoundName, missile.transform.position);
            Destroy(missile);
        }
        _missiles.Remove(entityId);
        _interpolator.Remove(entityId);

        if (_targetIndicators.TryGetValue(entityId, out GameObject indicator))
            Destroy(indicator);
        _targetIndicators.Remove(entityId);

        if (_loopSounds.TryGetValue(entityId, out PlayingSound loopSound))
        {
            loopSound.Stop(_flightLoopStopFadeOutDuration);
            _loopSounds.Remove(entityId);
        }

        if (_audioSources.TryGetValue(entityId, out CardRTSAudioSource audioSource))
        {
            audioSource.DestroyWhenIdle(); // lets the fade-out above finish before tearing down
            _audioSources.Remove(entityId);
        }
    }

    public void OnEntityActivated(ulong entityId) { }

    // No renderers to expose — see IComponentRenderer.GetRenderers.
    public IReadOnlyList<Renderer> GetRenderers(ulong entityId) => null;

    public void UpdateRenderable(List<ulong> entityIds)
    {
        if (_positionStore == null || _ballisticStore == null || _lifetimeStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_missiles.TryGetValue(id, out GameObject missile)) continue;
            if (!_ballisticStore.HasComponent(id) || !_positionStore.HasComponent(id) || !_lifetimeStore.HasComponent(id)) continue;

            BallisticProjectileComponent ballistic = _ballisticStore.GetComponent(id);
            LifetimeComponent lifetime = _lifetimeStore.GetComponent(id);

            // Same 0..1 elapsed-fraction AoeSpellRenderer derives from LifetimeComponent for
            // its own duration-elapsed material property.
            float t = lifetime.InitialTicksRemaining > 0
                ? 1f - (float)lifetime.TicksRemaining / lifetime.InitialTicksRemaining
                : 1f;
            t = Mathf.Clamp01(t);

            Vector3 launchPos = ToWorldPosition(_positionStore.GetComponent(id));
            Vector3 targetPos = ToWorldPosition(ballistic.TargetX, ballistic.TargetY);

            Vector3 flatPos = Vector3.Lerp(launchPos, targetPos, t);
            float arc = _arcHeight * 4f * t * (1f - t); // parabola: 0 at t=0/1, peak at t=0.5
            Vector3 tickPos = new Vector3(flatPos.x, flatPos.y + arc, flatPos.z);

            // tickPos only actually changes once per simulation tick (t is derived from
            // LifetimeComponent.TicksRemaining, which only decrements once per tick) — feeding
            // it through the interpolator smooths the visible motion across the frames
            // between ticks instead of the missile visibly stepping once per tick.
            Vector3 worldPos = _interpolator.Update(id, tickPos, isMoving: true);

            Vector3 previousPos = missile.transform.position;
            missile.transform.position = worldPos;

            Vector3 dir = worldPos - previousPos;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                missile.transform.rotation = _rotationDegreesPerSecond > 0f
                    ? Quaternion.RotateTowards(missile.transform.rotation, targetRotation, _rotationDegreesPerSecond * Time.deltaTime)
                    : targetRotation;
            }

            if (_audioSources.TryGetValue(id, out CardRTSAudioSource audioSource))
                audioSource.SetPosition(worldPos);
        }
    }

    private void PlaySoundAt(string soundName, Vector3 position)
    {
        if (string.IsNullOrEmpty(soundName) || AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShotAtPosition(soundName, position);
    }

    private void SpawnImpactEffect(Vector3 position)
    {
        if (_impactEffectPrefab == null) return;

        GameObject instance = Instantiate(_impactEffectPrefab, position, Quaternion.identity);
        if (_impactEffectDestroyAfterSeconds > 0f)
            Destroy(instance, _impactEffectDestroyAfterSeconds);
    }

    private Vector3 ToWorldPosition(float x, float y)
    {
        float h = 0f;
        if (WorldManager.instance?.Handler != null)
        {
            ushort maxTile = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
            ushort tx = (ushort)Mathf.Clamp(x, 0, maxTile);
            ushort ty = (ushort)Mathf.Clamp(y, 0, maxTile);
            h = WorldManager.instance.Handler.GetHeight(tx, ty);
        }

        return new Vector3(x, h, y);
    }

    private Vector3 ToWorldPosition(PositionComponent pos) => ToWorldPosition(pos.X, pos.Y);
}
