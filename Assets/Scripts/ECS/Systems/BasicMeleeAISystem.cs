using System;
using System.Collections.Generic;
using UnityEngine;

// Very small "aggro" melee AI. Each tick, per BasicMeleeAIComponent troop:
//  - If mid attack windup, just count it down and resolve it — nothing else happens.
//  - If a successful hit just landed, a cooldown (half the attack windup) counts down
//    before anything else happens, same as the windup.
//  - Otherwise, while not under an explicit player move order AND not in Passive mode
//    (AIModeComponent — see below), opportunistically auto-targets nearby enemies
//    (TargetingSystem.SetAutomaticTarget).
//  - Picks an active target: the closest player-assigned one always wins and is always
//    pursued (regardless of AI mode — an explicit player order is always honored); otherwise
//    the closest automatic one (never acquired at all in Passive mode).
//  - An automatically-acquired target is dropped (RemoveAutomaticTarget) once this troop
//    itself has strayed too far from its leash point in Guard mode; Aggressive never drops
//    one (chases indefinitely) and also re-homes its own leash to wherever it currently is
//    while it has an active target, so its "post" creeps along with the fight instead of
//    staying pinned to where it was first deployed.
//  - If the active target is within Range, stops and starts an attack windup
//    (AttackSpeed milliseconds, converted to ticks); otherwise chases it.
//  - With no target at all and no player move order in progress: Guard/Aggressive return to
//    their leash position (GoHome); Passive just stands down in place (Stop) — it never
//    moves or targets anything on its own, only in response to explicit player input
//    (a move order, or an explicit attack order via SetTargetsInput/TargetKind.PlayerAssigned).
//
// AIModeComponent is missing entirely on a troop not spawned through TroopCardHelper (e.g.
// SpawnTroopSystem's dev/test path) — GetMode defaults that case to Guard, same as
// TroopCardHelper's own default. See AIModeComponent.cs for what each mode means.
//
// Instance (not static) and registered per ECS, like PathfindingSystem/TargetingSystem,
// so a client's prediction ECS and the host's authoritative ECS keep independent state.
public class BasicMeleeAISystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private const int DefaultRange = 5;
    private const float DefaultAttackSpeedMilliseconds = 333f;
    private const int DefaultDamage = 10;
    private const float HomeRadius = 0.1f;

    private readonly List<ulong> _queryBuffer = new List<ulong>();
    private readonly HashSet<ulong> _movedThisTick = new HashSet<ulong>();

    private ECS _ecs;
    private ComponentStore<PositionComponent> _posStore;
    private ComponentStore<MovableComponent> _movStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<HealthComponent> _healthStore;
    private ComponentStore<AIModeComponent> _aiModeStore;
    private TargetingSystem _targeting;

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        _ecs = ecs;
        _posStore = ecs.GetComponentStore<PositionComponent>();
        _movStore = ecs.GetComponentStore<MovableComponent>();
        _troopStore = ecs.GetComponentStore<TroopComponent>();
        _healthStore = ecs.GetComponentStore<HealthComponent>();
        _aiModeStore = ecs.GetComponentStore<AIModeComponent>();
        _targeting = ecs.GetSystem<TargetingSystem>();
        if (_targeting == null) return;

        // Same tick's move orders, read ahead of PathfindingSystem (which applies them
        // later in this same Execute pass) so a windup can be cancelled the instant a
        // move order arrives for it, rather than one tick late.
        _movedThisTick.Clear();
        List<MoveTroopInput> moveInputs = ecs.GetInputsForTick<MoveTroopInput>();
        if (moveInputs != null)
        {
            foreach (MoveTroopInput input in moveInputs)
                foreach (MoveTroopInput.EntityDestination move in input.Moves)
                    _movedThisTick.Add(move.EntityId);
        }

        ComponentStore<BasicMeleeAIComponent> aiStore = ecs.GetComponentStore<BasicMeleeAIComponent>();
        aiStore.ForEach((ulong id) => Tick(id, aiStore));
    }

    private void Tick(ulong id, ComponentStore<BasicMeleeAIComponent> aiStore)
    {
        if (!_posStore.HasComponent(id) || !_movStore.HasComponent(id) || !_troopStore.HasComponent(id)) return;

        TroopComponent troop = _troopStore.GetComponent(id);
        if (!ActivationQuery.CanTakeActions(_ecs, id)) return;

        ref BasicMeleeAIComponent ai = ref aiStore.GetComponent(id);
        ref MovableComponent mov = ref _movStore.GetComponent(id);
        PositionComponent pos = _posStore.GetComponent(id);
        Vector2 myPos = new Vector2(pos.X, pos.Y);

        // Committed to an attack windup — ignore everything else until it resolves,
        // unless a fresh move order just came in for this troop: like an attack-move
        // cancel in League of Legends, that interrupts the windup immediately (no damage,
        // no cooldown) instead of finishing it out while sliding away.
        if (ai.AttackTargetId != 0)
        {
            if (_movedThisTick.Contains(id))
            {
                CancelAttack(id, ref ai);
                return;
            }
            ResolveAttack(id, ref ai, myPos);
            return;
        }

        // Post-attack cooldown — can't act again until it counts down to zero.
        if (ai.CooldownTicksRemaining > 0)
        {
            ai.CooldownTicksRemaining--;
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicMeleeAIComponent));
            return;
        }

        int range = StatsQuery.GetRange(_ecs, id, DefaultRange);
        AIMode mode = GetMode(id);

        if (mode != AIMode.Passive && !mov.playerDestinationSet)
            AcquireTargets(id, troop.OwnerPlayerId, myPos, range * ai.DetectionRangeMultiplier);

        ulong activeTarget = FindClosest(id, myPos, TargetKind.PlayerAssigned);

        if (activeTarget == 0 && mode != AIMode.Passive && !mov.playerDestinationSet)
        {
            activeTarget = FindClosest(id, myPos, TargetKind.Automatic);

            // Guard gives up a chase once IT (not the target) has wandered too far from its
            // leash post; Aggressive never gives up an automatic target at all.
            if (activeTarget != 0 && mode == AIMode.Guard)
            {
                Vector2 leashPos = new Vector2(mov.LeashX, mov.LeashY);
                if (Vector2.Distance(myPos, leashPos) > range * ai.ChaseRangeMultiplier)
                {
                    _targeting.RemoveAutomaticTarget(id, activeTarget);
                    activeTarget = 0;
                }
            }
        }

        if (activeTarget == 0)
        {
            if (mov.playerDestinationSet) return;

            if (mode == AIMode.Passive)
                Stop(id, ref mov);
            else
                GoHome(id, ref mov, myPos);
            return;
        }

        // Aggressive's leash creeps along with wherever it's currently fighting, instead of
        // staying pinned to its original post — see the class doc comment.
        if (mode == AIMode.Aggressive && (mov.LeashX != myPos.x || mov.LeashY != myPos.y))
        {
            mov.LeashX = myPos.x;
            mov.LeashY = myPos.y;
            _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
        }

        float dist = DistanceTo(activeTarget, myPos);
        if (dist <= range)
        {
            mov.currentMovementMode = MovementMode.NotMoving;
            ai.AttackTargetId = activeTarget;
            ai.AttackTicksRemaining = StatsQuery.GetAttackSpeed(_ecs, id, TickManager.MillisecondsToTicks(DefaultAttackSpeedMilliseconds));
            _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicMeleeAIComponent));
            _ecs.FlagEvents.Add(new TroopBeginAttackEvent { EntityId = id, TargetEntityId = activeTarget });
        }
        else
        {
            MoveToward(id, ref mov, ref ai, activeTarget, range);
        }
    }

    private void ResolveAttack(ulong id, ref BasicMeleeAIComponent ai, Vector2 myPos)
    {
        ai.AttackTicksRemaining--;
        if (ai.AttackTicksRemaining > 0)
        {
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicMeleeAIComponent));
            return;
        }

        ulong targetId = ai.AttackTargetId;
        int range = StatsQuery.GetRange(_ecs, id, DefaultRange);

        if (IsValidTarget(targetId) && DistanceTo(targetId, myPos) <= range * ai.AttackRangeMultiplier)
        {
            int damage = StatsQuery.GetDamage(_ecs, id, DefaultDamage);
            _ecs.Requests.CreateRequest(new DamageRequest(targetId, damage) { DealerEntityId = id });

            int attackSpeedTicks = StatsQuery.GetAttackSpeed(_ecs, id, TickManager.MillisecondsToTicks(DefaultAttackSpeedMilliseconds));
            ai.CooldownTicksRemaining = Mathf.RoundToInt(attackSpeedTicks * ai.CooldownMultiplier);
        }

        ai.AttackTargetId = 0;
        ai.AttackTicksRemaining = 0;
        _ecs.Delta.MarkComponentDirty(id, typeof(BasicMeleeAIComponent));
    }

    private void CancelAttack(ulong id, ref BasicMeleeAIComponent ai)
    {
        ai.AttackTargetId = 0;
        ai.AttackTicksRemaining = 0;
        _ecs.Delta.MarkComponentDirty(id, typeof(BasicMeleeAIComponent));
    }

    private void AcquireTargets(ulong id, ushort myOwnerId, Vector2 myPos, float detectionRange)
    {
        _queryBuffer.Clear();
        _ecs.ChunkTracker.GetEntitiesNear(myPos.x, myPos.y, detectionRange, _queryBuffer);

        foreach (ulong candidateId in _queryBuffer)
        {
            if (candidateId == id) continue;
            if (!IsEnemy(candidateId, myOwnerId)) continue;
            _targeting.SetAutomaticTarget(id, candidateId);
        }
    }

    private bool IsEnemy(ulong entityId, ushort myOwnerId)
    {
        if (!_healthStore.HasComponent(entityId)) return false;
        if (!_troopStore.HasComponent(entityId)) return false;

        TroopComponent other = _troopStore.GetComponent(entityId);
        if (other.OwnerPlayerId == myOwnerId) return false;
        return ActivationQuery.CanTakeActions(_ecs, entityId);
    }

    // A target is still worth chasing/attacking if it still exists, still has a
    // position, and (when trackable) isn't dead.
    private bool IsValidTarget(ulong entityId)
    {
        if (!_ecs.HasEntity(entityId)) return false;
        if (!_posStore.HasComponent(entityId)) return false;
        if (_healthStore.HasComponent(entityId) && _healthStore.GetComponent(entityId).CurrentHealth <= 0) return false;
        if (_troopStore.HasComponent(entityId) && _troopStore.GetComponent(entityId).IsDead) return false;
        return true;
    }

    private ulong FindClosest(ulong friendlyId, Vector2 myPos, TargetKind kind)
    {
        ulong bestId = 0;
        float bestDist = float.MaxValue;

        foreach (ulong targetId in _targeting.GetTargets(friendlyId))
        {
            if (_targeting.GetTargetKind(friendlyId, targetId) != kind) continue;
            if (!IsValidTarget(targetId)) continue;

            float dist = DistanceTo(targetId, myPos);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestId = targetId;
            }
        }

        return bestId;
    }

    private float DistanceTo(ulong entityId, Vector2 from)
    {
        PositionComponent p = _posStore.GetComponent(entityId);
        return Vector2.Distance(from, new Vector2(p.X, p.Y));
    }

    private AIMode GetMode(ulong id)
        => _aiModeStore != null && _aiModeStore.HasComponent(id) ? _aiModeStore.GetComponent(id).Mode : AIMode.Guard;

    // Passive's "no target" resting state — just stand down in place, unlike Guard/
    // Aggressive's GoHome, which actively walks back to the leash point.
    private void Stop(ulong id, ref MovableComponent mov)
    {
        if (mov.currentMovementMode == MovementMode.NotMoving) return;
        mov.currentMovementMode = MovementMode.NotMoving;
        _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
    }

    private void GoHome(ulong id, ref MovableComponent mov, Vector2 myPos)
    {
        float hdx = myPos.x - mov.LeashX, hdy = myPos.y - mov.LeashY;
        bool atHome = (hdx * hdx + hdy * hdy) < HomeRadius * HomeRadius;

        if (atHome)
        {
            if (mov.currentMovementMode == MovementMode.NotMoving) return;
            mov.currentMovementMode = MovementMode.NotMoving;
        }
        else
        {
            if (mov.currentMovementMode == MovementMode.MoveToDestination
                && mov.destinationX == mov.LeashX && mov.destinationY == mov.LeashY) return;

            mov.destinationX = mov.LeashX;
            mov.destinationY = mov.LeashY;
            mov.currentMovementMode = MovementMode.MoveToDestination;
        }

        _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
    }

    // Only recomputes the chase destination (and lets PathfindingSystem repath) once
    // the target has moved more than `range` from where it was the last time we did —
    // repathing on every tiny step of a moving target is wasteful. Switching to a
    // different target, or resuming movement after an attack windup (which clears
    // PathfindingSystem's cached path), always forces a fresh one.
    private void MoveToward(ulong id, ref MovableComponent mov, ref BasicMeleeAIComponent ai, ulong targetId, int range)
    {
        PositionComponent targetPos = _posStore.GetComponent(targetId);
        Vector2 targetVec = new Vector2(targetPos.X, targetPos.Y);

        bool modeNeedsFixing = mov.currentMovementMode != MovementMode.MoveToDestination;
        bool sameTarget = ai.LastPathTargetId == targetId;
        bool targetDrifted = !sameTarget
            || Vector2.Distance(targetVec, new Vector2(ai.LastPathTargetX, ai.LastPathTargetY)) > range;

        if (!modeNeedsFixing && !targetDrifted) return;

        if (modeNeedsFixing)
            mov.currentMovementMode = MovementMode.MoveToDestination;

        ai.LastPathTargetId = targetId;
        ai.LastPathTargetX = targetVec.x;
        ai.LastPathTargetY = targetVec.y;
        mov.destinationX = targetVec.x;
        mov.destinationY = targetVec.y;

        _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
        _ecs.Delta.MarkComponentDirty(id, typeof(BasicMeleeAIComponent));
    }
}
