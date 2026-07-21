using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders projectile entities driven by SkillshotProjectileSystem. Same shape as
/// SeekingProjectileRenderer (instantiate once per pooled entity, kept hidden while
/// pooled, shown/hidden off ProjectileActivatedEvent/ProjectileDeactivatedEvent,
/// interpolated position/facing while in flight) — the two are separate renderer
/// instances only because they're registered against different RenderableTypes (each
/// pool kind can then use its own prefab).
/// Register an instance with RenderableManager for RenderableType.SkillshotProjectile.
/// </summary>
public class SkillshotProjectileRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _prefab;
    [SerializeField] private float _rotationDegreesPerSecond = 1080f;
    [SerializeField] private float _groundOffset = 0f;

    [Header("Audio")]
    [SerializeField] private string _launchSoundName;
    [SerializeField] private string _impactSoundName;

    private ECS _ecs;
    private readonly Dictionary<ulong, GameObject> _objects = new();
    private readonly TickPositionInterpolator _interpolator = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;

        // See SeekingProjectileRenderer's Initialize comment: ecs's own FlagEvents fire
        // the instant BasicRangedAISystem/SkillshotProjectileSystem run locally (ahead of
        // any server round-trip) for responsiveness; ServerFlagEvents is the later,
        // authoritative confirmation. UpdateRenderable additionally re-syncs visibility to
        // the live (reconciled) ProjectileBaseComponent.IsActive every frame to correct any
        // mispredicted toggle.
        ecs.FlagEvents.Subscribe<ProjectileActivatedEvent>(OnProjectileActivated);
        ecs.FlagEvents.Subscribe<ProjectileDeactivatedEvent>(OnProjectileDeactivated);

        TickManager.instance.ServerFlagEvents.Subscribe<ProjectileActivatedEvent>(OnProjectileActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<ProjectileDeactivatedEvent>(OnProjectileDeactivated);

        // Audio subscribes only to the local/predicted stream above, NOT also
        // ServerFlagEvents — unlike the (idempotent) SetActive toggle above, playing a sound
        // twice for the same shot would be an audible double-trigger.
        ecs.FlagEvents.Subscribe<ProjectileActivatedEvent>(OnProjectileActivatedAudio);
        ecs.FlagEvents.Subscribe<ProjectileDeactivatedEvent>(OnProjectileDeactivatedAudio);
    }

    // ProjectileActivatedEvent/ProjectileDeactivatedEvent are raised for every projectile
    // in the game, not just this renderer's own — every renderer instance registered for a
    // different RenderableType/pool (e.g. a second SkillshotProjectileRenderer for a
    // different visual) hears the exact same events, so without this ownership check every
    // one of them would also play its own configured sound for somebody else's projectile.
    private void OnProjectileActivatedAudio(ProjectileActivatedEvent e)
    {
        if (_objects.ContainsKey(e.EntityId))
            PlaySoundAtEntity(_launchSoundName, e.EntityId);
    }

    private void OnProjectileDeactivatedAudio(ProjectileDeactivatedEvent e)
    {
        if (_objects.ContainsKey(e.EntityId))
            PlaySoundAtEntity(_impactSoundName, e.EntityId);
    }

    private void OnProjectileActivated(ProjectileActivatedEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out GameObject go))
            go.SetActive(true);

        // A pooled projectile reuses the same entity id across every shot it's fired for —
        // without this, the interpolator's leftover sample from wherever it deactivated
        // LAST time is still sitting there, so the first UpdateRenderable after this
        // activation would lerp from that stale old position all the way to the new firing
        // point instead of snapping straight there (briefly showing it far from the
        // shooter, then rapidly sliding into place).
        _interpolator.Remove(e.EntityId);
    }

    private void OnProjectileDeactivated(ProjectileDeactivatedEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out GameObject go))
            go.SetActive(false);
        _interpolator.Remove(e.EntityId);
    }

    // Reads the live ECS position rather than the GameObject's transform - at the instant
    // activation fires the transform may still hold last cycle's (pre-UpdateRenderable) spot.
    private void PlaySoundAtEntity(string soundName, ulong entityId)
    {
        if (string.IsNullOrEmpty(soundName) || AudioManager.instance == null) return;

        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(entityId)) return;

        AudioManager.instance.PlayOneShotAtPosition(soundName, ToWorldPosition(posStore.GetComponent(entityId)));
    }

    public void OnEntityAdded(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _prefab == null) return;

        GameObject go = Instantiate(_prefab);
        go.name = $"SkillshotProjectile_{entityId}";
        go.SetActive(false); // hidden while pooled; ProjectileActivatedEvent reveals it
        _objects[entityId] = go;
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out GameObject go))
            Destroy(go);
        _objects.Remove(entityId);
        _interpolator.Remove(entityId);
    }

    // Projectiles have no activation-delay concept (no TroopComponent) — they're shown/
    // hidden directly off ProjectileActivatedEvent/ProjectileDeactivatedEvent instead.
    public void OnEntityActivated(ulong entityId) { }

    // No renderers to expose — see IComponentRenderer.GetRenderers.
    public IReadOnlyList<Renderer> GetRenderers(ulong entityId) => null;

    public void UpdateRenderable(List<ulong> entityIds)
    {
        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        var projectileStore = _ecs?.GetComponentStore<ProjectileBaseComponent>();
        if (posStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_objects.TryGetValue(id, out GameObject go)) continue;
            if (!posStore.HasComponent(id)) continue;

            bool isActive = projectileStore != null && projectileStore.HasComponent(id)
                && projectileStore.GetComponent(id).IsActive;

            if (go.activeSelf != isActive)
                go.SetActive(isActive);

            Vector3 worldPos = ToWorldPosition(posStore.GetComponent(id));
            go.transform.position = _interpolator.Update(id, worldPos, isActive);

            if (isActive)
            {
                Vector3 dir = _interpolator.GetLastMoveDirection(id);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    go.transform.rotation = Quaternion.RotateTowards(
                        go.transform.rotation, targetRotation, _rotationDegreesPerSecond * Time.deltaTime);
                }
            }
        }
    }

    private Vector3 ToWorldPosition(PositionComponent pos)
    {
        float h = 0f;
        if (WorldManager.instance?.Handler != null)
        {
            ushort maxTile = (ushort)(WorldGenHandler.CHUNK_SIZE_TILES * WorldGenHandler.WorldSizeChunks - 1);
            ushort tx = (ushort)Mathf.Clamp(pos.X, 0, maxTile);
            ushort ty = (ushort)Mathf.Clamp(pos.Y, 0, maxTile);
            h = WorldManager.instance.Handler.GetHeight(tx, ty);
        }

        return new Vector3(pos.X, h + _groundOffset, pos.Y);
    }
}
