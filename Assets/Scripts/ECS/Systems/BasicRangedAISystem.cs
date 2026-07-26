using System;
using System.Collections.Generic;
using UnityEngine;

// Ranged counterpart to BasicMeleeAISystem — same aggro/chase/windup/cooldown shape (and the
// same AIModeComponent-driven Passive/Guard/Aggressive behavior — see BasicMeleeAISystem's
// class doc comment for the full breakdown), but resolving an attack fires a pooled
// projectile (ProjectilePool.Fire) instead of dealing damage directly. Each tick, per
// BasicRangedAIComponent troop:
//  - If mid windup, just count it down and resolve it — nothing else happens.
//  - If a shot was just fired, a wind-down (same length as the windup) counts down
//    before anything else happens, same as the windup.
//  - Otherwise, while not under an explicit player move order AND not in Passive mode,
//    opportunistically auto-targets nearby enemies (TargetingSystem.SetAutomaticTarget).
//  - Picks an active target: the closest player-assigned one always wins and is always
//    pursued; otherwise the closest automatic one (never acquired in Passive mode) —
//    dropped (RemoveAutomaticTarget) once this troop has strayed too far from its leash
//    point in Guard mode, never dropped in Aggressive mode.
//  - If the active target is within Range, stops and starts a windup (AttackSpeed
//    milliseconds, converted to ticks); otherwise chases it.
//  - Once the windup finishes, fires a projectile at the target if it's still within
//    range * AttackRangeMultiplier — otherwise the shot is wasted (no projectile, no
//    wind-down), the same way BasicMeleeAISystem's windup can resolve without landing a hit.
//  - With no target at all and no player move order in progress: Guard/Aggressive return to
//    their leash position (GoHome); Passive just stands down in place (Stop).
// Instance (not static) and registered per ECS, like BasicMeleeAISystem, so a client's
// prediction ECS and the host's authoritative ECS keep independent state.
public class BasicRangedAISystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private const int DefaultRange = 8;
    private const float DefaultAttackSpeedMilliseconds = 800f;
    private const float HomeRadius = 0.1f;

    // Extra chase-repath drift tolerance per world unit of distance between this troop and
    // its target — the further away a chase currently is, the less a small drift in the
    // target's exact position actually matters for the overall path, so recomputing gets
    // delayed proportionally more. See MoveToward.
    private const float DriftToleranceDistanceRatio = 0.15f;
    // Max extra, per-chasing-troop-id deterministic jitter added on top of the drift
    // tolerance above, so a large group of troops all chasing the same distant target
    // doesn't all cross their threshold on the exact same tick and repath in one
    // simultaneous lag spike — see DeterministicJitter.
    private const float DriftToleranceJitterAmplitude = 1.5f;

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

        ComponentStore<BasicRangedAIComponent> aiStore = ecs.GetComponentStore<BasicRangedAIComponent>();
        aiStore.ForEach((ulong id) => Tick(id, aiStore));
    }

    private void Tick(ulong id, ComponentStore<BasicRangedAIComponent> aiStore)
    {
        if (!_posStore.HasComponent(id) || !_movStore.HasComponent(id) || !_troopStore.HasComponent(id)) return;

        TroopComponent troop = _troopStore.GetComponent(id);
        if (!ActivationQuery.IsActivated(_ecs, id)) return;

        ref BasicRangedAIComponent ai = ref aiStore.GetComponent(id);
        ref MovableComponent mov = ref _movStore.GetComponent(id);
        PositionComponent pos = _posStore.GetComponent(id);
        Vector2 myPos = new Vector2(pos.X, pos.Y);

        // A fresh move order abandons whatever this troop was chasing/attacking, no matter
        // which state below it's currently sitting in (mid-windup, post-shot wind-down,
        // mid-chase) — clearing LastPathTargetId here, before any of those early-return
        // branches, covers all of them in one place. Without this, a move order landing
        // during e.g. WindDownTicksRemaining's return (right below) would never reach the
        // activeTarget==0 -> Stop/GoHome path that normally clears it, leaving it stranded at
        // the old target until Tick's LastPathTargetId fallback (see its own comment) picks it
        // back up the instant playerDestinationSet clears at the new destination — walking
        // back to re-attack a target the player told it to abandon.
        if (_movedThisTick.Contains(id) && ai.LastPathTargetId != 0)
        {
            ai.LastPathTargetId = 0;
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
        }

        // Committed to a windup — ignore everything else until it resolves, unless a
        // fresh move order just came in for this troop: like an attack-move cancel in
        // League of Legends, that interrupts the windup immediately (no shot fired, no
        // wind-down) instead of finishing it out while sliding away.
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

        // Post-shot wind-down — can't act again until it counts down to zero.
        if (ai.WindDownTicksRemaining > 0)
        {
            ai.WindDownTicksRemaining--;
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
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

        // Fallback for a client that doesn't own this troop, and so never received the
        // SetTargetsInput that may have assigned its current PlayerAssigned target —
        // TargetingSystem's _targets dict is built purely from locally-processed inputs
        // (InputBuffer/NetworkManager.OnClientTickInput only ever forwards a client's raw
        // inputs to the server, never to other clients), so a non-owning client's copy of
        // this troop can go the whole fight without ever seeing that entry. LastPathTargetId
        // is genuine, replicated component data instead — whichever target the authoritative
        // simulation actually last chased — so fall back to it before concluding there's
        // really no target. This matters most in Passive mode, which has no other safety net
        // at all (AcquireTargets and the Automatic-kind fallback above are both skipped
        // entirely for Passive), so without this a Passive troop's target is invisible to
        // every client except its owner.
        //
        // _movedThisTick also gates this, same as the attack-windup cancel check above and
        // for the same reason: PathfindingSystem (which actually writes playerDestinationSet
        // true) runs after this system, so on the very tick a move order arrives,
        // playerDestinationSet still reads false here. Without this guard, a "clear targets
        // then move" cancel (SelectionManager's PerformPointTargetOrMove, both landing in the
        // same tick) would pass the !playerDestinationSet check on that same tick, letting
        // this fallback resurrect the just-cleared LastPathTargetId — on the authoritative
        // simulation itself, not just an observer — before Stop/GoHome ever gets a chance to
        // run and clear it, permanently stranding the troop chasing a target it was told to
        // abandon (with no targeting indicator, since _targets really is empty).
        if (activeTarget == 0 && !mov.playerDestinationSet && !_movedThisTick.Contains(id)
            && ai.LastPathTargetId != 0 && IsValidTarget(ai.LastPathTargetId))
            activeTarget = ai.LastPathTargetId;

        if (activeTarget == 0)
        {
            if (mov.playerDestinationSet) return;

            if (mode == AIMode.Passive)
                Stop(id, ref ai, ref mov);
            else
                GoHome(id, ref ai, ref mov, myPos);
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
            // In range but can't currently initiate an attack (e.g. silenced) — hold
            // position near the target instead of starting a windup or wastefully chasing.
            if (!ActivationQuery.CanPerform(_ecs, id))
            {
                mov.currentMovementMode = MovementMode.NotMoving;
                _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
                return;
            }

            mov.currentMovementMode = MovementMode.NotMoving;
            ai.AttackTargetId = activeTarget;
            ai.AttackTicksRemaining = StatsQuery.GetAttackSpeed(_ecs, id, TickManager.MillisecondsToTicks(DefaultAttackSpeedMilliseconds));
            _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
            _ecs.FlagEvents.Add(new TroopBeginAttackEvent { EntityId = id, TargetEntityId = activeTarget, X = myPos.x, Y = myPos.y });
        }
        else
        {
            MoveToward(id, ref mov, ref ai, activeTarget, range, myPos);
        }
    }

    private void ResolveAttack(ulong id, ref BasicRangedAIComponent ai, Vector2 myPos)
    {
        ai.AttackTicksRemaining--;
        if (ai.AttackTicksRemaining > 0)
        {
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
            return;
        }

        ulong targetId = ai.AttackTargetId;
        int range = StatsQuery.GetRange(_ecs, id, DefaultRange);

        // Finishing the attack (firing the shot) needs its own CanPerform check — a
        // silence landing mid-windup should waste the shot, same as the target having
        // stepped out of range, rather than still firing because the windup already
        // started.
        if (IsValidTarget(targetId) && DistanceTo(targetId, myPos) <= range * ai.AttackRangeMultiplier
            && ActivationQuery.CanPerform(_ecs, id))
        {
            if (ProjectilePool.Fire(_ecs, id, targetId, myPos) != 0)
            {
                int attackSpeedTicks = StatsQuery.GetAttackSpeed(_ecs, id, TickManager.MillisecondsToTicks(DefaultAttackSpeedMilliseconds));
                ai.WindDownTicksRemaining = Mathf.RoundToInt(attackSpeedTicks * ai.WindDownMultiplier);
            }
        }

        ai.AttackTargetId = 0;
        ai.AttackTicksRemaining = 0;
        _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
        _ecs.FlagEvents.Add(new AttackWindupFinishedEvent { EntityId = id, X = myPos.x, Y = myPos.y });
    }

    private void CancelAttack(ulong id, ref BasicRangedAIComponent ai)
    {
        ai.AttackTargetId = 0;
        ai.AttackTicksRemaining = 0;
        _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
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
        return ActivationQuery.IsActivated(_ecs, entityId);
    }

    // A target is still worth chasing/attacking if it still exists, still has a
    // position, and (when trackable) isn't dead. Every entityId reaching this is already
    // guaranteed non-owned (added via IsEnemy's own owner check, or a player's right-click
    // order — see ShadowCloakSystem.IsCloaked's own doc comment), so a cloaked target can
    // simply be rejected outright here, same as an IsDead one, with no owner check needed.
    private bool IsValidTarget(ulong entityId)
    {
        if (!_ecs.HasEntity(entityId)) return false;
        if (!_posStore.HasComponent(entityId)) return false;
        if (_healthStore.HasComponent(entityId) && _healthStore.GetComponent(entityId).CurrentHealth <= 0) return false;
        if (_troopStore.HasComponent(entityId) && _troopStore.GetComponent(entityId).IsDead) return false;
        if (ShadowCloakSystem.IsCloaked(_ecs, entityId, out _)) return false;
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
    // Aggressive's GoHome, which actively walks back to the leash point. Also clears
    // LastPathTargetId — a genuine give-up (as opposed to just not knowing) must be
    // reflected in replicated component data, or a client observing this troop (which
    // falls back to LastPathTargetId when it has no local record of the target — see
    // Tick's activeTarget resolution) would keep treating the abandoned target as active
    // forever once it's replicated to them.
    private void Stop(ulong id, ref BasicRangedAIComponent ai, ref MovableComponent mov)
    {
        if (ai.LastPathTargetId != 0)
        {
            ai.LastPathTargetId = 0;
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
        }

        if (mov.currentMovementMode == MovementMode.NotMoving) return;
        mov.currentMovementMode = MovementMode.NotMoving;
        _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
    }

    private void GoHome(ulong id, ref BasicRangedAIComponent ai, ref MovableComponent mov, Vector2 myPos)
    {
        if (ai.LastPathTargetId != 0)
        {
            ai.LastPathTargetId = 0;
            _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
        }

        if (!ActivationQuery.CanMove(_ecs, id)) return;

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

    // Only recomputes the chase destination (and lets PathfindingSystem repath) once the
    // target has moved more than a drift tolerance from where it was the last time we did —
    // repathing on every tiny step of a moving target is wasteful. That tolerance is at
    // least `range`, plus DriftToleranceDistanceRatio for every world unit currently between
    // this troop and its target (a distant chase can tolerate more absolute drift before it
    // meaningfully changes the path), plus a small amount of DeterministicJitter unique to
    // this troop's own id — without that last part, a whole group chasing the same distant
    // target would all cross the exact same distance-based threshold on the exact same tick
    // and repath simultaneously, which is its own lag spike even though each individual
    // repath is "correctly" delayed. Switching to a different target, or resuming movement
    // after a windup (which clears PathfindingSystem's cached path), always forces a fresh
    // one regardless of drift.
    private void MoveToward(ulong id, ref MovableComponent mov, ref BasicRangedAIComponent ai, ulong targetId, int range, Vector2 myPos)
    {
        if (!ActivationQuery.CanMove(_ecs, id)) return;

        PositionComponent targetPos = _posStore.GetComponent(targetId);
        Vector2 targetVec = new Vector2(targetPos.X, targetPos.Y);

        bool modeNeedsFixing = mov.currentMovementMode != MovementMode.MoveToDestination;
        bool sameTarget = ai.LastPathTargetId == targetId;

        float distanceToTarget = Vector2.Distance(myPos, targetVec);
        float driftTolerance = range + distanceToTarget * DriftToleranceDistanceRatio
            + DeterministicJitter(id, DriftToleranceJitterAmplitude);
        bool targetDrifted = !sameTarget
            || Vector2.Distance(targetVec, new Vector2(ai.LastPathTargetX, ai.LastPathTargetY)) > driftTolerance;

        if (!modeNeedsFixing && !targetDrifted) return;

        if (modeNeedsFixing)
            mov.currentMovementMode = MovementMode.MoveToDestination;

        ai.LastPathTargetId = targetId;
        ai.LastPathTargetX = targetVec.x;
        ai.LastPathTargetY = targetVec.y;
        mov.destinationX = targetVec.x;
        mov.destinationY = targetVec.y;

        _ecs.Delta.MarkComponentDirty(id, typeof(MovableComponent));
        _ecs.Delta.MarkComponentDirty(id, typeof(BasicRangedAIComponent));
    }

    // Deterministic, stable pseudo-random value in [0, amplitude) derived purely from id —
    // deliberately NOT Unity's Random or ulong.GetHashCode (neither is guaranteed to produce
    // the identical result on the server and every predicting client, which this must, since
    // it feeds a lockstep-replicated repath decision). A splitmix64-style integer mix: not
    // cryptographic, just needs to spread different ids out evenly and repeatably.
    private static float DeterministicJitter(ulong id, float amplitude)
    {
        ulong x = id + 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        float unit = (x & 0xFFFFFF) / (float)0x1000000; // low 24 bits -> [0, 1)
        return unit * amplitude;
    }
}
