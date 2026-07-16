using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renders troop entities driven by BasicMeleeAISystem. Instantiates a BasicMeleeGameObject
/// prefab per entity, smoothly interpolates it between the two most recent simulation-tick
/// positions, and fires animator triggers off server-authoritative flag events.
/// Register an instance with RenderableManager for RenderableType.BasicMelee.
/// </summary>
public class BasicTroopRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private BasicTroopGameObject _prefab;
    [SerializeField] private float _rotationDegreesPerSecond = 540f;

    [Header("Audio")]
    [SerializeField] private string _attackWindupSoundName;
    [SerializeField] private string _dealDamageSoundName;
    [SerializeField] private string _takeDamageSoundName;
    [SerializeField] private string _deathSoundName;

    // Capsule primitive (used by the prefab's placeholder mesh, if any) is 2 units tall;
    // offset by 1 so it stands on the ground plane.
    [SerializeField] private float _attackAnimationDuration = 1;
    [SerializeField] private float _attackAnimationTimeUntilImpact = 0.5f;
    private const float GroundOffset = 0f;

    // The Speed stat value the running animation was authored/tuned at — the Animator's
    // MovementSpeed param is set to (actual Speed stat) / this, so the run cycle plays back
    // at its original, non-sliding pace for a troop with exactly this Speed, and scales up/
    // down for anything faster/slower (e.g. from a StatModifierComponent buff/debuff).
    [SerializeField] private float _runAnimationSpeedStat = 5f;

    // How often (seconds) to recompute/set MovementSpeed — a troop's Speed stat only
    // changes when a modifier is applied/expires, not every frame, so this doesn't need
    // per-frame precision.
    [SerializeField] private float _speedParamUpdateInterval = 0.25f;

    private const int DefaultSpeed = 10;

    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int DieTrigger = Animator.StringToHash("Die");
    private static readonly int IsMovingParam = Animator.StringToHash("IsMoving");
    private static readonly int AttackSpeedMultiplierFloat = Animator.StringToHash("AttackSpeedMultiplier");
    private static readonly int MovementSpeedFloat = Animator.StringToHash("MovementSpeed");

    private ECS _ecs;
    private readonly Dictionary<ulong, BasicTroopGameObject> _objects = new();
    private readonly TickPositionInterpolator _interpolator = new();

    // Shared across all entities this renderer owns, rather than tracked per entity —
    // MovementSpeed just gets recalculated for the whole batch once this elapses (see
    // UpdateRenderable), instead of staggering individual entities' updates.
    private float _speedUpdateTimer;

    public void Initialize(ECS ecs)
    {
        _ecs = ecs;
        TickManager.instance.ServerFlagEvents.Subscribe<TroopBeginAttackEvent>(OnTroopBeginAttack);
        TickManager.instance.ServerFlagEvents.Subscribe<TroopDiedEvent>(OnTroopDied);
        TickManager.instance.ServerFlagEvents.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    private void OnTroopBeginAttack(TroopBeginAttackEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out BasicTroopGameObject go))
        {
            if (go.Animator != null)
            {
                int attackSpeedTicks = StatsQuery.GetAttackSpeed(_ecs, e.EntityId, TickManager.MillisecondsToTicks(1000f));
                float windupSeconds = TickManager.TicksToSeconds(attackSpeedTicks);
                float multiplier = _attackAnimationTimeUntilImpact / windupSeconds;
                go.Animator.SetFloat(AttackSpeedMultiplierFloat, multiplier);
                go.Animator.SetTrigger(AttackTrigger);
            }

            PlaySoundAt(_attackWindupSoundName, go.transform.position);
        }

        FaceTarget(e.EntityId, e.TargetEntityId);
    }

    // Dealer/target are separate troops (usually) — each gets its own sound lookup, so a
    // troop with only one of the two fields configured still gets that half.
    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (_objects.TryGetValue(e.DealerEntityId, out BasicTroopGameObject dealerGo))
            PlaySoundAt(_dealDamageSoundName, dealerGo.transform.position);

        if (_objects.TryGetValue(e.EntityId, out BasicTroopGameObject targetGo))
            PlaySoundAt(_takeDamageSoundName, targetGo.transform.position);
    }

    private void PlaySoundAt(string soundName, Vector3 position)
    {
        if (string.IsNullOrEmpty(soundName) || AudioManager.instance == null) return;
        AudioManager.instance.PlayOneShotAtPosition(soundName, position);
    }

    // Snaps the troop to face its target the instant the attack windup starts. This is
    // stable for the whole windup: BasicMeleeAISystem sets MovableComponent to NotMoving
    // before firing this event, so UpdateRenderable's movement-direction rotation (gated
    // on isMoving) won't run and fight with it until the troop starts moving again.
    private void FaceTarget(ulong entityId, ulong targetEntityId)
    {
        if (targetEntityId == 0) return;
        if (!_objects.TryGetValue(entityId, out BasicTroopGameObject go)) return;

        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(targetEntityId)) return;

        Vector3 targetPos = ToWorldPosition(posStore.GetComponent(targetEntityId));
        Vector3 dir = targetPos - go.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.0001f) return;

        go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private void OnTroopDied(TroopDiedEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out BasicTroopGameObject go))
        {
            if (go.Animator != null) go.Animator.SetTrigger(DieTrigger);
            PlaySoundAt(_deathSoundName, go.transform.position);
        }
    }

    public void OnEntityAdded(ulong entityId)
    {
        // No visual yet — the prefab is spawned on activation (see OnEntityActivated)
        // so troops stay invisible until then.
    }

    public void OnEntityRemoved(ulong entityId)
    {
        if (_objects.TryGetValue(entityId, out BasicTroopGameObject go))
            Destroy(go.gameObject);
        _objects.Remove(entityId);
        _interpolator.Remove(entityId);
    }

    public void OnEntityActivated(ulong entityId)
    {
        if (_objects.ContainsKey(entityId) || _prefab == null) return;

        BasicTroopGameObject go = Instantiate(_prefab);
        go.name = $"Troop_{entityId}";
        _objects[entityId] = go;
    }

    public void UpdateRenderable(List<ulong> entityIds)
    {
        var posStore = _ecs?.GetComponentStore<PositionComponent>();
        var movStore = _ecs?.GetComponentStore<MovableComponent>();
        if (posStore == null) return;

        _speedUpdateTimer += Time.deltaTime;
        bool updateSpeedParam = _speedUpdateTimer >= _speedParamUpdateInterval;
        if (updateSpeedParam)
            _speedUpdateTimer -= _speedParamUpdateInterval;

        foreach (ulong id in entityIds)
        {
            if (!_objects.TryGetValue(id, out BasicTroopGameObject go)) continue;
            if (!posStore.HasComponent(id)) continue;

            bool isMoving = movStore != null && movStore.HasComponent(id)
                && movStore.GetComponent(id).currentMovementMode != MovementMode.NotMoving;

            Vector3 worldPos = ToWorldPosition(posStore.GetComponent(id));
            go.transform.position = _interpolator.Update(id, worldPos, isMoving);

            if (isMoving)
            {
                Vector3 dir = _interpolator.GetLastMoveDirection(id);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    go.transform.rotation = Quaternion.RotateTowards(
                        go.transform.rotation, targetRotation, _rotationDegreesPerSecond * Time.deltaTime);
                }
            }

            if (go.Animator != null)
            {
                go.Animator.SetBool(IsMovingParam, isMoving);

                if (updateSpeedParam && _runAnimationSpeedStat > 0f)
                {
                    int speed = StatsQuery.GetSpeed(_ecs, id, DefaultSpeed);
                    go.Animator.SetFloat(MovementSpeedFloat, speed / _runAnimationSpeedStat);
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
