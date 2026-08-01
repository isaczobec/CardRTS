using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders TornadoProjectileComponent entities (TornadoCard). Unlike SkillshotProjectileRenderer
/// — which gates visibility on a pooled ProjectileBaseComponent.IsActive / ProjectileActivatedEvent,
/// neither of which a one-off TornadoCard entity ever has, so that renderer just instantiates the
/// prefab and immediately hides it forever — this instead mirrors BasicTroopRenderer's own shape:
/// the prefab is only spawned once ActivatableComponent's own delay finishes (OnEntityActivated),
/// matching the card's "after an activation delay spawn a projectile" design exactly, and
/// destroyed again once the entity's LifetimeComponent naturally expires it at the end of its
/// sweep (OnEntityRemoved). PositionComponent is moved directly by TornadoProjectileSystem every
/// tick it's active (not computed from a Lifetime-derived fraction the way MissileProjectileRenderer's
/// own arc is), so this only needs to read + interpolate it like any ordinary moving entity, and
/// derives its facing from the interpolated position delta rather than a stored direction field.
/// Register an instance with RenderableManager for RenderableType.Tornado.
/// </summary>
public class TornadoProjectileRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _prefab;
    [SerializeField] private float _rotationDegreesPerSecond = 180f;
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
    }

    // No visual yet — the prefab is spawned on activation (see OnEntityActivated), mirrors
    // BasicTroopRenderer's own reasoning: the entity exists (and its ActivatableComponent
    // countdown is already ticking) before the tornado has actually formed.
    public void OnEntityAdded(ulong entityId) { }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out GameObject go))
        {
            PlaySoundAt(_impactSoundName, go.transform.position);
            Destroy(go);
        }
        _objects.Remove(entityId);
        _interpolator.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _prefab == null) return;

        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        Vector3 launchPos = posStore != null && posStore.HasComponent(entityId)
            ? ToWorldPosition(posStore.GetComponent(entityId))
            : Vector3.zero;

        GameObject go = Instantiate(_prefab, launchPos, Quaternion.identity);
        go.name = $"Tornado_{entityId}";
        _objects[entityId] = go;

        PlaySoundAt(_launchSoundName, launchPos);
    }

    private void PlaySoundAt(string soundName, Vector3 position)
    {
        if (string.IsNullOrEmpty(soundName) || AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShotAtPosition(soundName, position);
    }

    // No renderers to expose — see IComponentRenderer.GetRenderers.
    public IReadOnlyList<Renderer> GetRenderers(ulong entityId) => null;

    public void UpdateRenderable(List<ulong> entityIds)
    {
        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        if (posStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_objects.TryGetValue(id, out GameObject go)) continue;
            if (!posStore.HasComponent(id)) continue;

            Vector3 worldPos = ToWorldPosition(posStore.GetComponent(id));
            Vector3 previousPos = go.transform.position;

            // Always treated as moving — TornadoProjectileSystem only reveals this entity
            // (via OnEntityActivated above) once it's already past ActivatableComponent's
            // delay, at which point it's continuously sweeping toward its destination every
            // tick (see that system's own comment) until LifetimeComponent removes it.
            go.transform.position = _interpolator.Update(id, worldPos, isMoving: true);

            Vector3 dir = go.transform.position - previousPos;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                go.transform.rotation = _rotationDegreesPerSecond > 0f
                    ? Quaternion.RotateTowards(go.transform.rotation, targetRotation, _rotationDegreesPerSecond * Time.deltaTime)
                    : targetRotation;
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
