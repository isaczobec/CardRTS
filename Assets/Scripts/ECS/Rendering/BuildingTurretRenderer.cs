using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders turret-style building entities driven by TurretAISystem (see CannonCard). Unlike
/// BasicBuildingRenderer, this fires attack animations/sounds off the same TroopBeginAttack
/// Event/AttackWindupFinishedEvent/DamageDealtEvent/TroopDiedEvent flag events
/// VariedAttackTroopRenderer does — reusing its AttackAnimation/AttackAnimationSelectionMode
/// types directly, "similar animator interface" — and every frame rotates whichever child
/// transforms the spawned TurretGameObject lists (_rotatingParts) to face the turret's
/// current target (TurretAIComponent.TargetEntityId), read straight from the ECS rather than
/// from any flag event. The building's own root transform is placed once on activation and
/// never touched again — buildings are stationary, so (like BasicBuildingRenderer) there's no
/// per-frame position interpolation for it, just for the aiming parts' rotation.
///
/// Register an instance with RenderableManager for RenderableType.Cannon.
/// </summary>
public class BuildingTurretRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private TurretGameObject _prefab;

    [Header("Attack Animation")]
    // Animations to pick from on each attack — see _selectionMode for how. An entry with a
    // blank TriggerName is skipped (treated as "no trigger fired that attack") rather than
    // erroring. Reuses VariedAttackTroopRenderer's own AttackAnimation/
    // AttackAnimationSelectionMode types directly.
    [SerializeField] private List<AttackAnimation> _attackAnimations = new List<AttackAnimation>();
    [SerializeField] private AttackAnimationSelectionMode _selectionMode = AttackAnimationSelectionMode.Random;

    [Header("Aiming")]
    // Degrees/second _rotatingParts turn to face the current target — 0 or less snaps
    // instantly instead of easing.
    [SerializeField] private float _aimRotationDegreesPerSecond = 720f;

    [Header("Audio")]
    [SerializeField] private string _attackWindupSoundName;
    // Played on AttackWindupFinishedEvent — fires the instant an attack windup resolves,
    // whether or not the shot actually landed/fired. Distinct from _dealDamageSoundName,
    // which only plays on an actual hit (DamageDealtEvent).
    [SerializeField] private string _attackWindupFinishedSoundName;
    [SerializeField] private string _dealDamageSoundName;
    [SerializeField] private string _takeDamageSoundName;
    [SerializeField] private string _deathSoundName;
    // Played once, at the turret's spawn position, the instant it activates — e.g. a
    // deploy/power-up sound. Skipped entirely when blank. Mirrors VariedAttackTroopRenderer's
    // own _activationSoundName.
    [SerializeField] private string _activationSoundName;

    private const float GroundOffset = 0f;

    private static readonly int AttackSpeedMultiplierFloat = Animator.StringToHash("AttackSpeedMultiplier");
    private static readonly int DieTrigger = Animator.StringToHash("Die");

    private ECS _ecs;
    private ComponentStore<PositionComponent> _positionStore;
    private ComponentStore<TurretAIComponent> _turretAiStore;
    private readonly Dictionary<ulong, TurretGameObject> _objects = new();

    // Per-entity next index into _attackAnimations, for AttackAnimationSelectionMode.Cycle
    // only — mirrors VariedAttackTroopRenderer's own _nextAttackIndex.
    private readonly Dictionary<ulong, int> _nextAttackIndex = new();

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        _positionStore = ecs.GetComponentStore<PositionComponent>();
        _turretAiStore = ecs.GetComponentStore<TurretAIComponent>();

        TickManager.instance.ServerFlagEvents.Subscribe<TroopBeginAttackEvent>(OnTroopBeginAttack);
        TickManager.instance.ServerFlagEvents.Subscribe<AttackWindupFinishedEvent>(OnAttackWindupFinished);
        TickManager.instance.ServerFlagEvents.Subscribe<TroopDiedEvent>(OnTroopDied);
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    private void OnTroopBeginAttack(TroopBeginAttackEvent e)
    {
        if (!_objects.TryGetValue(e.EntityId, out TurretGameObject go)) return;

        if (go.Animator != null)
        {
            AttackAnimation animation = ResolveAttackAnimation(e.EntityId);
            if (animation != null && !string.IsNullOrEmpty(animation.TriggerName))
            {
                int attackSpeedTicks = StatsQuery.GetAttackSpeed(_ecs, e.EntityId, TickManager.MillisecondsToTicks(1000f));
                float windupSeconds = TickManager.TicksToSeconds(attackSpeedTicks);
                float multiplier = windupSeconds > 0f ? animation.TimeUntilImpact / windupSeconds : 1f;
                go.Animator.SetFloat(AttackSpeedMultiplierFloat, multiplier);
                go.Animator.SetTrigger(Animator.StringToHash(animation.TriggerName));
            }
        }

        PlaySoundAt(_attackWindupSoundName, go.transform.position);
    }

    // Picks the next AttackAnimation for entityId per _selectionMode. Returns null if the
    // list is empty. Mirrors VariedAttackTroopRenderer.ResolveAttackAnimation exactly.
    private AttackAnimation ResolveAttackAnimation(ulong entityId)
    {
        if (_attackAnimations == null || _attackAnimations.Count == 0) return null;

        if (_selectionMode == AttackAnimationSelectionMode.Random)
            return _attackAnimations[Random.Range(0, _attackAnimations.Count)];

        _nextAttackIndex.TryGetValue(entityId, out int index);
        index %= _attackAnimations.Count;
        _nextAttackIndex[entityId] = (index + 1) % _attackAnimations.Count;
        return _attackAnimations[index];
    }

    private void OnAttackWindupFinished(AttackWindupFinishedEvent e)
    {
        if (!_objects.TryGetValue(e.EntityId, out TurretGameObject go)) return;
        PlaySoundAt(_attackWindupFinishedSoundName, go.transform.position);
    }

    // Dealer/target are separate entities (usually) — each gets its own sound lookup, so a
    // turret with only one of the two fields configured still gets that half.
    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_objects.TryGetValue(e.DealerEntityId, out TurretGameObject dealerGo))
            PlaySoundAt(_dealDamageSoundName, dealerGo.transform.position);

        if (_objects.TryGetValue(e.EntityId, out TurretGameObject targetGo))
            PlaySoundAt(_takeDamageSoundName, targetGo.transform.position);
    }

    private void OnTroopDied(TroopDiedEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out TurretGameObject go))
        {
            if (go.Animator != null) go.Animator.SetTrigger(DieTrigger);
            PlaySoundAt(_deathSoundName, go.transform.position);
        }
    }

    private void PlaySoundAt(string soundName, Vector3 position)
    {
        if (string.IsNullOrEmpty(soundName) || AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShotAtPosition(soundName, position);
    }

    public void OnEntityAdded(ulong entityId)
    {
        // No visual yet — spawned on activation (see OnEntityActivated), same deploy-delay
        // convention every other renderer in this project follows.
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out TurretGameObject go))
            Destroy(go.gameObject);
        _objects.Remove(entityId);
        _nextAttackIndex.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _prefab == null) return;
        if (_positionStore == null || !_positionStore.HasComponent(entityId)) return;

        TurretGameObject go = Instantiate(_prefab);
        go.name = $"Turret_{entityId}";
        go.transform.position = ToWorldPosition(_positionStore.GetComponent(entityId));
        _objects[entityId] = go;

        PlaySoundAt(_activationSoundName, go.transform.position);
    }

    public IReadOnlyList<Renderer> GetRenderers(ulong entityId)
        => _objects.TryGetValue(entityId, out TurretGameObject go) ? go.Renderers : null;

    // The building itself never moves, so unlike BasicTroopRenderer/VariedAttackTroopRenderer
    // there's no per-frame position/rotation to apply to its root — only the aiming parts,
    // driven straight off TurretAIComponent.TargetEntityId (no flag event for "this is what
    // I'm currently aiming at" exists, since it can change every tick as targets come and go
    // — reading it live here is simpler than inventing one).
    public void UpdateRenderable(List<ulong> entityIds)
    {
        if (_turretAiStore == null || _positionStore == null) return;

        foreach (ulong id in entityIds)
        {
            if (!_objects.TryGetValue(id, out TurretGameObject go)) continue;
            if (!_turretAiStore.HasComponent(id)) continue;

            ulong targetId = _turretAiStore.GetComponent(id).TargetEntityId;
            if (targetId == 0 || !_positionStore.HasComponent(targetId)) continue;

            AimAt(go, _positionStore.GetComponent(targetId));
        }
    }

    private void AimAt(TurretGameObject go, PositionComponent targetPos)
    {
        Vector3 worldTarget = ToWorldPosition(targetPos);

        foreach (Transform part in go.RotatingParts)
        {
            if (part == null) continue;

            Vector3 dir = worldTarget - part.position;
            dir.y = 0f;
            if (dir.sqrMagnitude <= 0.0001f) continue;

            Quaternion targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            part.rotation = _aimRotationDegreesPerSecond > 0f
                ? Quaternion.RotateTowards(part.rotation, targetRotation, _aimRotationDegreesPerSecond * Time.deltaTime)
                : targetRotation;
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
