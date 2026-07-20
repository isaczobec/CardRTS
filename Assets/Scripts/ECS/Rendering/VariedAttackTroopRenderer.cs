using System.Collections.Generic;
using UnityEngine;

public enum AttackAnimationSelectionMode
{
    // Advances through _attackAnimations in order, one step per attack, wrapping back to
    // the start once the end is reached. Each entity tracks its own position in the
    // sequence independently of every other entity.
    Cycle,

    // Picks uniformly at random from _attackAnimations on every attack (repeats allowed).
    Random,
}

// One entry in _attackAnimations — a single Animator trigger plus the timing that goes with
// specifically that animation (a slow heavy swing and a quick jab don't necessarily want the
// same TimeUntilImpact).
[System.Serializable]
public class AttackAnimation
{
    public string TriggerName;
    public float Duration = 1f;
    public float TimeUntilImpact = 0.5f;
}

// One entry in _abilityAnimationTriggers — fires TriggerName whenever the troop performs
// whichever ability it has equipped in AbilitySlot. AbilitySlot is 0-3 (Q/W/E/R, same
// convention as AbilityComponent.GetAbilityId), not an AbilityManager ability ID, so this
// stays correct even if a different troop equips a different ability in that slot.
[System.Serializable]
public class AbilityAnimationTrigger
{
    public int AbilitySlot;
    public string TriggerName;

    // If set, the Animator's SpeedParam float is set to SpeedMultiplier right before
    // TriggerName fires — e.g. so a longer/shorter ability windup can stretch or compress
    // the animation clip to match, the same way OnTroopBeginAttack does for attacks.
    // Skipped entirely (no SetFloat call) when blank.
    public string SpeedParam;
    public float SpeedMultiplier = 1f;

    // If true, the troop instantly turns to face AbilityPerformedEvent's WorldX/WorldY
    // before TriggerName fires — e.g. so a cast animation faces the cursor/target even if
    // the troop wasn't already moving that direction. Mirrors OnTroopBeginAttack's own
    // FaceTarget snap for attacks.
    public bool RotateTowardPosition;

    // Played (one-shot, at the troop's position) alongside TriggerName — e.g. a cast/fire
    // sound distinct per ability, as opposed to _abilityWindupSoundName's single shared
    // sound for every ability's windup. Skipped entirely when blank.
    public string SoundName;
}

/// <summary>
/// Same as BasicTroopRenderer, except its attack animation is chosen from a list
/// (_attackAnimations) each time TroopBeginAttackEvent fires, instead of a single fixed
/// "Attack" trigger with one shared timing — either cycling through the list in order or
/// picking one at random, per _selectionMode. Everything else (movement, death, damage
/// sounds, speed param) is identical to BasicTroopRenderer; kept as its own standalone
/// script rather than sharing a base class with it, so this variant can evolve independently
/// without any risk to existing BasicMelee/BasicRanged troops.
///
/// Register an instance with RenderableManager for whichever RenderableType should use
/// varied attack animations — a second/third renderer instance can be pointed at any
/// RenderableType, same as BasicTroopRenderer's own doc comment describes.
/// </summary>
public class VariedAttackTroopRenderer : MonoBehaviour, IComponentRenderer
{
    [SerializeField] private BasicTroopGameObject _prefab;
    [SerializeField] private float _rotationDegreesPerSecond = 540f;

    [Header("Attack Animation")]
    // Animations to pick from on each attack — see _selectionMode for how. An entry with a
    // blank TriggerName is skipped (treated as "no trigger fired that attack") rather than
    // erroring.
    [SerializeField] private List<AttackAnimation> _attackAnimations = new List<AttackAnimation>();
    [SerializeField] private AttackAnimationSelectionMode _selectionMode = AttackAnimationSelectionMode.Random;

    [Header("Ability Animations")]
    // Fires TriggerName whenever AbilityPerformedEvent reports this troop used the
    // matching slot — see AbilityAnimationTrigger. More than one entry can match the same
    // slot; all of them fire.
    [SerializeField] private List<AbilityAnimationTrigger> _abilityAnimationTriggers = new List<AbilityAnimationTrigger>();

    [Header("Audio")]
    [SerializeField] private string _attackWindupSoundName;
    // Played on AttackWindupFinishedEvent — fires the instant a normal attack windup
    // (BasicMeleeAISystem/BasicRangedAISystem) resolves, whether or not the hit/shot
    // actually landed. Distinct from _dealDamageSoundName, which only plays on an actual
    // hit (DamageDealtEvent).
    [SerializeField] private string _attackWindupFinishedSoundName;
    // Played on AttackWindupBeganEvent — shared across every ability this troop has, as
    // opposed to AbilityAnimationTrigger.SoundName's per-ability cast/fire sound.
    [SerializeField] private string _abilityWindupSoundName;
    [SerializeField] private string _dealDamageSoundName;
    [SerializeField] private string _takeDamageSoundName;
    [SerializeField] private string _deathSoundName;

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

    private static readonly int DieTrigger = Animator.StringToHash("Die");
    private static readonly int IsMovingParam = Animator.StringToHash("IsMoving");
    private static readonly int AttackSpeedMultiplierFloat = Animator.StringToHash("AttackSpeedMultiplier");
    private static readonly int MovementSpeedFloat = Animator.StringToHash("MovementSpeed");

    private ECS _ecs;
    private readonly Dictionary<ulong, BasicTroopGameObject> _objects = new();
    private readonly TickPositionInterpolator _interpolator = new();

    // Per-entity next index into _attackAnimations, for AttackAnimationSelectionMode.Cycle
    // only — each troop advances through the list independently of every other troop.
    private readonly Dictionary<ulong, int> _nextAttackIndex = new();

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
        TickManager.instance.ServerFlagEvents.Subscribe<AbilityPerformedEvent>(OnAbilityPerformed);
        TickManager.instance.ServerFlagEvents.Subscribe<AttackWindupBeganEvent>(OnAttackWindupBegan);
        TickManager.instance.ServerFlagEvents.Subscribe<AttackWindupFinishedEvent>(OnAttackWindupFinished);
    }

    private void OnAbilityPerformed(AbilityPerformedEvent e)
    {
        if (!_objects.TryGetValue(e.EntityId, out BasicTroopGameObject go)) return;

        foreach (AbilityAnimationTrigger trigger in _abilityAnimationTriggers)
        {
            if (trigger.AbilitySlot != e.Slot) continue;

            if (trigger.RotateTowardPosition)
                FacePosition(e.EntityId, e.WorldX, e.WorldY);

            if (go.Animator != null)
            {
                if (!string.IsNullOrEmpty(trigger.SpeedParam))
                    go.Animator.SetFloat(Animator.StringToHash(trigger.SpeedParam), trigger.SpeedMultiplier);

                if (!string.IsNullOrEmpty(trigger.TriggerName))
                    go.Animator.SetTrigger(Animator.StringToHash(trigger.TriggerName));
            }

            PlaySoundAt(trigger.SoundName, go.transform.position);
        }
    }

    private void OnAttackWindupBegan(AttackWindupBeganEvent e)
    {
        if (!_objects.TryGetValue(e.EntityId, out BasicTroopGameObject go)) return;
        PlaySoundAt(_abilityWindupSoundName, go.transform.position);
    }

    // Fires for both BasicMeleeAISystem and BasicRangedAISystem — both raise the exact same
    // AttackWindupFinishedEvent from their own ResolveAttack once the windup's ticks reach
    // 0, regardless of which AI component the troop has, so no branching is needed here.
    private void OnAttackWindupFinished(AttackWindupFinishedEvent e)
    {
        if (!_objects.TryGetValue(e.EntityId, out BasicTroopGameObject go)) return;
        PlaySoundAt(_attackWindupFinishedSoundName, go.transform.position);
    }

    // Snaps the troop to face a raw world point (as opposed to FaceTarget's live entity
    // lookup) — used for AbilityAnimationTrigger.RotateTowardPosition, where
    // AbilityPerformedEvent only ever carries a point, not a target entity id.
    private void FacePosition(ulong entityId, float worldX, float worldY)
    {
        if (!_objects.TryGetValue(entityId, out BasicTroopGameObject go)) return;

        Vector3 targetPos = new Vector3(worldX, go.transform.position.y, worldY);
        Vector3 dir = targetPos - go.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.0001f) return;

        go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private void OnTroopBeginAttack(TroopBeginAttackEvent e)
    {
        if (_objects.TryGetValue(e.EntityId, out BasicTroopGameObject go))
        {
            if (go.Animator != null)
            {
                AttackAnimation animation = ResolveAttackAnimation(e.EntityId);
                if (animation != null && !string.IsNullOrEmpty(animation.TriggerName))
                {
                    int attackSpeedTicks = StatsQuery.GetAttackSpeed(_ecs, e.EntityId, TickManager.MillisecondsToTicks(1000f));
                    float windupSeconds = TickManager.TicksToSeconds(attackSpeedTicks);
                    float multiplier = animation.TimeUntilImpact / windupSeconds;
                    go.Animator.SetFloat(AttackSpeedMultiplierFloat, multiplier);
                    go.Animator.SetTrigger(Animator.StringToHash(animation.TriggerName));
                }
            }

            PlaySoundAt(_attackWindupSoundName, go.transform.position);
        }

        FaceTarget(e.EntityId, e.TargetEntityId);
    }

    // Picks the next AttackAnimation for entityId per _selectionMode. Returns null if the
    // list is empty.
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
        _nextAttackIndex.Remove(entityId);
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
                && movStore.GetComponent(id).IsMoving;
            bool teleported = movStore != null && movStore.HasComponent(id)
                && movStore.GetComponent(id).TeleportedTick == _ecs.CurrentSimulationTick;

            Vector3 worldPos = ToWorldPosition(posStore.GetComponent(id));
            go.transform.position = _interpolator.Update(id, worldPos, isMoving, teleported);

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
