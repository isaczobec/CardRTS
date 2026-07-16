using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders projectile entities driven by SeekingProjectileSystem. Instantiates a plain
/// prefab per pooled projectile entity (created once, kept hidden while pooled), shows/
/// hides it off ProjectileActivatedEvent/ProjectileDeactivatedEvent, and interpolates its
/// position/facing between the two most recent simulation-tick positions while in flight.
/// Register an instance with RenderableManager for RenderableType.SeekingProjectile.
/// </summary>
public class SeekingProjectileRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _prefab;
    [SerializeField] private float _rotationDegreesPerSecond = 1080f;

    [Header("Audio")]
    [SerializeField] private string _launchSoundName;
    [SerializeField] private string _impactSoundName;

    private const float GroundOffset = 0f;

    private ECS _ecs;
    private readonly Dictionary<ulong, GameObject> _objects = new();
    private readonly TickPositionInterpolator _interpolator = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;

        // ecs here is the active (prediction) ECS on clients/host — its own FlagEvents
        // fire the instant BasicRangedAISystem/SeekingProjectileSystem run locally, ahead
        // of any server round-trip, for responsiveness. ServerFlagEvents is the later,
        // authoritative confirmation. The two can disagree if a shot was mispredicted, so
        // UpdateRenderable additionally re-syncs visibility to the live (reconciled)
        // ProjectileBaseComponent.IsActive every frame — that always reflects the latest
        // server-confirmed truth after reconciliation, so it wins within one frame even
        // if a local prediction guessed wrong.
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

    private void OnProjectileActivated(ProjectileActivatedEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out GameObject go))
            go.SetActive(true);
    }

    private void OnProjectileDeactivated(ProjectileDeactivatedEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out GameObject go))
            go.SetActive(false);
        _interpolator.Remove(e.EntityId);
    }

    private void OnProjectileActivatedAudio(ProjectileActivatedEvent e) => PlaySoundAtEntity(_launchSoundName, e.EntityId);
    private void OnProjectileDeactivatedAudio(ProjectileDeactivatedEvent e) => PlaySoundAtEntity(_impactSoundName, e.EntityId);

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
        go.name = $"Projectile_{entityId}";
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

            // Belt-and-suspenders for a mispredicted activation/deactivation: this reads
            // the live (reconciled) component state every frame, which always ends up
            // matching the server, so it corrects any event-driven toggle that guessed
            // wrong instead of leaving a "ghost" projectile visible (or an active one
            // hidden).
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

        return new Vector3(pos.X, h + GroundOffset, pos.Y);
    }
}
