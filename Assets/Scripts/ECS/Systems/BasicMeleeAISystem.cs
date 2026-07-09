using System;
using System.Collections.Generic;
using UnityEngine;

// Very small "aggro" melee AI. Each tick, per BasicMeleeAIComponent troop:
//  - If mid attack windup, just count it down and resolve it — nothing else happens.
//  - If a successful hit just landed, a cooldown (half the attack windup) counts down
//    before anything else happens, same as the windup.
//  - Otherwise, while not under an explicit player move order, opportunistically
//    auto-targets nearby enemies (TargetingSystem.SetAutomaticTarget).
//  - Picks an active target: the closest player-assigned one always wins and is always
//    pursued; otherwise the closest automatic one, which is dropped
//    (RemoveAutomaticTarget) if it strays beyond the chase range.
//  - If the active target is within Range, stops and starts an attack windup
//    (AttackSpeed milliseconds, converted to ticks); otherwise chases it.
//  - With no target at all (and no player move order in progress), returns to its
//    original ("leash") position.
// Instance (not static) and registered per ECS, like PathfindingSystem/TargetingSystem,
// so a client's prediction ECS and the host's authoritative ECS keep independent state.
public class BasicMeleeAISystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private const int DefaultRange = 5;
    private const float DefaultAttackSpeedMilliseconds = 1000f;
    private const int DefaultDamage = 10;
    private const float HomeRadius = 0.1f;

    private readonly List<ulong> _queryBuffer = new List<ulong>();

    private ECS _ecs;
    private ComponentStore<PositionComponent> _posStore;
    private ComponentStore<MovableComponent> _movStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<HealthComponent> _healthStore;
    private TargetingSystem _targeting;

    public void Execute(ECS ecs)
    {
        _ecs = ecs;
        _posStore = ecs.GetComponentStore<PositionComponent>();
        _movStore = ecs.GetComponentStore<MovableComponent>();
        _troopStore = ecs.GetComponentStore<TroopComponent>();
        _healthStore = ecs.GetComponentStore<HealthComponent>();
        _targeting = ecs.GetSystem<TargetingSystem>();
        if (_targeting == null) return;

        ComponentStore<BasicMeleeAIComponent> aiStore = ecs.GetComponentStore<BasicMeleeAIComponent>();
        aiStore.ForEach((ulong id) => Tick(id, aiStore));
    }

    private void Tick(ulong id, ComponentStore<BasicMeleeAIComponent> aiStore)
    {
        if (!_posStore.HasComponent(id) || !_movStore.HasComponent(id) || !_troopStore.HasComponent(id)) return;

        TroopComponent troop = _troopStore.GetComponent(id);
        if (!troop.CanTakeActions) return;

        ref BasicMeleeAIComponent ai = ref aiStore.GetComponent(id);
        ref MovableComponent mov = ref _movStore.GetComponent(id);
        PositionComponent pos = _posStore.GetComponent(id);
        Vector2 myPos = new Vector2(pos.X, pos.Y);

        // Committed to an attack windup — ignore everything else until it resolves.
        if (ai.AttackTargetId != 0)
        {
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

        if (!mov.playerDestinationSet)
            AcquireTargets(id, troop.OwnerPlayerId, myPos, range * ai.DetectionRangeMultiplier);

        ulong activeTarget = FindClosest(id, myPos, TargetKind.PlayerAssigned);

        if (activeTarget == 0 && !mov.playerDestinationSet)
        {
            activeTarget = FindClosest(id, myPos, TargetKind.Automatic);
            if (activeTarget != 0 && DistanceTo(activeTarget, myPos) > range * ai.ChaseRangeMultiplier)
            {
                _targeting.RemoveAutomaticTarget(id, activeTarget);
                activeTarget = 0;
            }
        }

        if (activeTarget == 0)
        {
            if (!mov.playerDestinationSet)
                GoHome(id, ref ai, ref mov, myPos);
            return;
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
            ai.CooldownTicksRemaining = attackSpeedTicks / 2;
        }

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
        return other.CanTakeActions;
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

    private void GoHome(ulong id, ref BasicMeleeAIComponent ai, ref MovableComponent mov, Vector2 myPos)
    {
        float hdx = myPos.x - ai.OriginalX, hdy = myPos.y - ai.OriginalY;
        bool atHome = (hdx * hdx + hdy * hdy) < HomeRadius * HomeRadius;

        if (atHome)
        {
            if (mov.currentMovementMode == MovementMode.NotMoving) return;
            mov.currentMovementMode = MovementMode.NotMoving;
        }
        else
        {
            if (mov.currentMovementMode == MovementMode.MoveToDestination
                && mov.destinationX == ai.OriginalX && mov.destinationY == ai.OriginalY) return;

            mov.destinationX = ai.OriginalX;
            mov.destinationY = ai.OriginalY;
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
