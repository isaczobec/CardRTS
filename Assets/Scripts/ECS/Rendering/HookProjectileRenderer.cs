using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders RenderableType.Hook projectile entities (PirateCard's Hook ability) — a pooled
/// entity created once at troop-spawn time and shown/hidden off ProjectileActivatedEvent/
/// ProjectileDeactivatedEvent rather than added/removed per shot, mirroring
/// SeekingProjectileRenderer. Additionally draws a LineRenderer between the hook's own live
/// (interpolated) position and whichever Pirate most recently fired it — learned via
/// LinkedProjectileFiredEvent (see FireProjectileOnExpireSystem), refreshed every time this
/// same pooled entity is fired again, since a pooled projectile reuses the same entity id
/// across every shot.
///
/// Register an instance with RenderableManager for RenderableType.Hook.
/// </summary>
public class HookProjectileRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private GameObject _hookPrefab;
    [SerializeField] private float _rotationDegreesPerSecond = 1080f;
    [SerializeField] private float _groundOffset = 0f;

    [Header("Line")]
    [SerializeField] private Material _lineMaterial;
    [SerializeField] private float _lineWidth = 0.15f;
    [SerializeField] private Color _lineColor = Color.white;

    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;

    private readonly Dictionary<ulong, GameObject> _hookObjects = new();
    private readonly Dictionary<ulong, LineRenderer> _lineRenderers = new();
    // hook projectile entity id -> the Pirate (or whoever) that most recently fired it.
    private readonly Dictionary<ulong, ulong> _linkedCasterByHookId = new();
    private readonly TickPositionInterpolator _interpolator = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _positionStore = ecs.GetComponentStore<PositionComponent>();

        // ecs here is the active (prediction) ECS on clients/host — see
        // SeekingProjectileRenderer's own Initialize comment for why show/hide subscribes to
        // both the local/predicted stream and the later authoritative one, while the link
        // itself (just bookkeeping, safe to set redundantly) only needs the local one.
        ecs.FlagEvents.Subscribe<ProjectileActivatedEvent>(OnProjectileActivated);
        ecs.FlagEvents.Subscribe<ProjectileDeactivatedEvent>(OnProjectileDeactivated);
        ecs.FlagEvents.Subscribe<LinkedProjectileFiredEvent>(OnLinkedProjectileFired);

        TickManager.instance.ServerFlagEvents.Subscribe<ProjectileActivatedEvent>(OnProjectileActivated);
        TickManager.instance.ServerFlagEvents.Subscribe<ProjectileDeactivatedEvent>(OnProjectileDeactivated);
    }

    private void OnLinkedProjectileFired(LinkedProjectileFiredEvent e)
    {
        if (!_hookObjects.ContainsKey(e.EntityId)) return; // not one of ours

        _linkedCasterByHookId[e.EntityId] = e.LinkedEntityId;
        // A fresh shot — don't lerp from wherever this same pooled entity deactivated last
        // time (see SeekingProjectileRenderer.OnProjectileActivated's own reasoning).
        _interpolator.Remove(e.EntityId);
    }

    private void OnProjectileActivated(ProjectileActivatedEvent e)
    {
        if (_hookObjects.TryGetValue(e.EntityId, out GameObject go))
            go.SetActive(true);
        if (_lineRenderers.TryGetValue(e.EntityId, out LineRenderer line))
            line.enabled = true;
    }

    private void OnProjectileDeactivated(ProjectileDeactivatedEvent e)
    {
        if (_hookObjects.TryGetValue(e.EntityId, out GameObject go))
            go.SetActive(false);
        if (_lineRenderers.TryGetValue(e.EntityId, out LineRenderer line))
            line.enabled = false;
        _interpolator.Remove(e.EntityId);
    }

    public void OnEntityAdded(ulong entityId)
    {
        if (_hookObjects.ContainsKey(entityId)) return;

        GameObject hook = _hookPrefab != null ? Instantiate(_hookPrefab) : new GameObject();
        hook.name = $"Hook_{entityId}";
        hook.SetActive(false); // hidden while pooled; ProjectileActivatedEvent reveals it
        _hookObjects[entityId] = hook;

        GameObject lineObject = new GameObject($"HookLine_{entityId}");
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = _lineWidth;
        line.endWidth = _lineWidth;
        line.useWorldSpace = true;
        if (_lineMaterial != null) line.material = _lineMaterial;
        line.startColor = _lineColor;
        line.endColor = _lineColor;
        line.enabled = false; // shown/hidden alongside the hook itself
        _lineRenderers[entityId] = line;
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_hookObjects.TryGetValue(entityId, out GameObject hook))
            Destroy(hook);
        _hookObjects.Remove(entityId);

        if (_lineRenderers.TryGetValue(entityId, out LineRenderer line))
            Destroy(line.gameObject);
        _lineRenderers.Remove(entityId);

        _linkedCasterByHookId.Remove(entityId);
        _interpolator.Remove(entityId);
    }

    // Pooled projectiles have no activation-delay concept — shown/hidden directly off
    // ProjectileActivatedEvent/ProjectileDeactivatedEvent instead, mirroring
    // SeekingProjectileRenderer.
    public void OnEntityActivated(ulong entityId) { }

    // No renderers to expose — see IComponentRenderer.GetRenderers.
    public IReadOnlyList<Renderer> GetRenderers(ulong entityId) => null;

    public void UpdateRenderable(List<ulong> entityIds)
    {
        ComponentStore<ProjectileBaseComponent> projectileStore = _ecs?.GetComponentStore<ProjectileBaseComponent>();
        if (_positionStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_hookObjects.TryGetValue(id, out GameObject hook)) continue;
            if (!_positionStore.HasComponent(id)) continue;

            bool isActive = projectileStore != null && projectileStore.HasComponent(id)
                && projectileStore.GetComponent(id).IsActive;

            // Belt-and-suspenders for a mispredicted activation/deactivation — mirrors
            // SeekingProjectileRenderer's own reasoning.
            if (hook.activeSelf != isActive)
                hook.SetActive(isActive);

            _lineRenderers.TryGetValue(id, out LineRenderer line);
            if (line != null && line.enabled != isActive)
                line.enabled = isActive;

            Vector3 worldPos = ToWorldPosition(_positionStore.GetComponent(id));
            Vector3 interpolatedPos = _interpolator.Update(id, worldPos, isActive);
            hook.transform.position = interpolatedPos;

            if (isActive)
            {
                Vector3 dir = _interpolator.GetLastMoveDirection(id);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    hook.transform.rotation = Quaternion.RotateTowards(
                        hook.transform.rotation, targetRotation, _rotationDegreesPerSecond * Time.deltaTime);
                }

                if (line != null && _linkedCasterByHookId.TryGetValue(id, out ulong casterId) && _positionStore.HasComponent(casterId))
                {
                    Vector3 casterPos = ToWorldPosition(_positionStore.GetComponent(casterId));
                    line.SetPosition(0, casterPos);
                    line.SetPosition(1, interpolatedPos);
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
