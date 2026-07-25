using System;
using System.Collections.Generic;
using UnityEngine;

// AI for stationary defensive buildings (see CannonCard/TurretAIComponent) — fires the
// turret's own pooled seeking projectiles at whichever enemy troop is currently closest and
// within its own Range stat, locking onto it across multiple attack cycles until it leaves
// Range, dies, or is otherwise invalidated (see IsValidTarget), at which point the turret
// locks onto the next-closest qualifying enemy instead. Deliberately never reads
// SetTargetsInput/TargetingSystem at all, unlike BasicMeleeAISystem/BasicRangedAISystem — a
// turret's target can never be manually assigned by a player.
//
// No movement/chase/leash concerns at all (buildings have no MovableComponent), so this is
// considerably simpler than BasicMeleeAISystem/BasicRangedAISystem: a target is either
// currently within Range or it isn't, with nothing to walk toward in between.
//
// Instance (not static) and registered per ECS, like BasicMeleeAISystem/BasicRangedAISystem,
// so a client's prediction ECS and the host's authoritative ECS keep independent state.
public class TurretAISystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private const int DefaultRange = 10;
    private const float DefaultAttackSpeedMilliseconds = 1000f;

    private readonly List<ulong> _queryBuffer = new List<ulong>();

    private ECS _ecs;
    private ComponentStore<PositionComponent> _posStore;
    private ComponentStore<TroopComponent> _troopStore;
    private ComponentStore<HealthComponent> _healthStore;

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        _ecs = ecs;
        _posStore = ecs.GetComponentStore<PositionComponent>();
        _troopStore = ecs.GetComponentStore<TroopComponent>();
        _healthStore = ecs.GetComponentStore<HealthComponent>();

        ComponentStore<TurretAIComponent> aiStore = ecs.GetComponentStore<TurretAIComponent>();
        aiStore.ForEach((ulong id) => Tick(id, aiStore));
    }

    private void Tick(ulong id, ComponentStore<TurretAIComponent> aiStore)
    {
        if (!_posStore.HasComponent(id) || !_troopStore.HasComponent(id)) return;
        if (!ActivationQuery.IsActivated(_ecs, id)) return;

        ref TurretAIComponent ai = ref aiStore.GetComponent(id);
        TroopComponent troop = _troopStore.GetComponent(id);
        PositionComponent pos = _posStore.GetComponent(id);
        Vector2 myPos = new Vector2(pos.X, pos.Y);

        // Post-shot wind-down — can't act again until it counts down to zero.
        if (ai.WindDownTicksRemaining > 0)
        {
            ai.WindDownTicksRemaining--;
            _ecs.Delta.MarkComponentDirty(id, typeof(TurretAIComponent));
            return;
        }

        float range = StatsQuery.GetRange(_ecs, id, DefaultRange);

        // Committed to a windup — ignore everything else until it resolves. Nothing can
        // cancel a turret's windup early (unlike a troop's move-order cancel) since it never
        // moves and can't be given orders at all.
        if (ai.AttackTicksRemaining > 0)
        {
            ResolveAttack(id, ref ai, troop.OwnerPlayerId, myPos, range);
            return;
        }

        // (Re)validate the current lock, or acquire a fresh one.
        if (ai.TargetEntityId != 0 && !IsValidTarget(ai.TargetEntityId, troop.OwnerPlayerId, myPos, range))
            ai.TargetEntityId = 0;

        if (ai.TargetEntityId == 0)
            ai.TargetEntityId = FindClosestEnemyInRange(id, troop.OwnerPlayerId, myPos, range);

        if (ai.TargetEntityId == 0)
        {
            _ecs.Delta.MarkComponentDirty(id, typeof(TurretAIComponent));
            return;
        }

        // In range but can't currently initiate an attack (e.g. silenced) — just wait.
        if (!ActivationQuery.CanPerform(_ecs, id)) return;

        ai.AttackTicksRemaining = StatsQuery.GetAttackSpeed(_ecs, id, TickManager.MillisecondsToTicks(DefaultAttackSpeedMilliseconds));
        _ecs.Delta.MarkComponentDirty(id, typeof(TurretAIComponent));
        _ecs.FlagEvents.Add(new TroopBeginAttackEvent { EntityId = id, TargetEntityId = ai.TargetEntityId });
    }

    private void ResolveAttack(ulong id, ref TurretAIComponent ai, ushort ownerPlayerId, Vector2 myPos, float range)
    {
        ai.AttackTicksRemaining--;
        if (ai.AttackTicksRemaining > 0)
        {
            _ecs.Delta.MarkComponentDirty(id, typeof(TurretAIComponent));
            return;
        }

        ulong targetId = ai.TargetEntityId;

        // Finishing the attack (firing the shot) needs its own validity + CanPerform check —
        // a silence landing mid-windup should waste the shot, same as the target having
        // left range/died, rather than still firing because the windup already started.
        if (targetId != 0 && IsValidTarget(targetId, ownerPlayerId, myPos, range * ai.AttackRangeMultiplier)
            && ActivationQuery.CanPerform(_ecs, id))
        {
            if (ProjectilePool.Fire(_ecs, id, targetId, myPos) != 0)
            {
                int attackSpeedTicks = StatsQuery.GetAttackSpeed(_ecs, id, TickManager.MillisecondsToTicks(DefaultAttackSpeedMilliseconds));
                ai.WindDownTicksRemaining = Mathf.RoundToInt(attackSpeedTicks * ai.WindDownMultiplier);
            }
        }
        else
        {
            // Invalidated mid-windup — drop it so the very next tick looks for a fresh
            // target instead of waiting on one that's already gone.
            ai.TargetEntityId = 0;
        }

        _ecs.Delta.MarkComponentDirty(id, typeof(TurretAIComponent));
        _ecs.FlagEvents.Add(new AttackWindupFinishedEvent { EntityId = id });
    }

    private bool IsValidTarget(ulong targetId, ushort myOwnerId, Vector2 myPos, float range)
    {
        if (!_ecs.HasEntity(targetId)) return false;
        if (!_posStore.HasComponent(targetId) || !_troopStore.HasComponent(targetId)) return false;

        TroopComponent target = _troopStore.GetComponent(targetId);
        if (target.OwnerPlayerId == myOwnerId) return false;
        if (target.IsDead) return false;
        if (!target.IsPhysicalTroop) return false; // turrets only ever engage real troops, not other buildings

        if (_healthStore != null && _healthStore.HasComponent(targetId) && _healthStore.GetComponent(targetId).CurrentHealth <= 0) return false;
        if (!ActivationQuery.IsActivated(_ecs, targetId)) return false;
        if (ShadowCloakSystem.IsCloaked(_ecs, targetId, out _)) return false;

        PositionComponent targetPos = _posStore.GetComponent(targetId);
        float dx = targetPos.X - myPos.x, dy = targetPos.Y - myPos.y;
        return dx * dx + dy * dy <= range * range;
    }

    private ulong FindClosestEnemyInRange(ulong turretId, ushort myOwnerId, Vector2 myPos, float range)
    {
        _queryBuffer.Clear();
        _ecs.ChunkTracker.GetEntitiesNear(myPos.x, myPos.y, range, _queryBuffer);

        ulong bestId = 0;
        float bestDistSq = float.MaxValue;

        foreach (ulong candidateId in _queryBuffer)
        {
            if (candidateId == turretId) continue;
            if (!IsValidTarget(candidateId, myOwnerId, myPos, range)) continue;

            PositionComponent p = _posStore.GetComponent(candidateId);
            float dx = p.X - myPos.x, dy = p.Y - myPos.y;
            float distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestId = candidateId;
            }
        }

        return bestId;
    }
}
