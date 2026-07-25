using System;
using System.Collections.Generic;
using UnityEngine;

// AI for stationary defensive buildings (see CannonCard/MissileSiloCard/TurretAIComponent) —
// fires at whichever enemy troop is currently closest and within its own Range stat, locking
// onto it across multiple attack cycles until it leaves Range, dies, or is otherwise
// invalidated, at which point the turret locks onto the next-closest qualifying enemy
// instead. Deliberately never reads SetTargetsInput/TargetingSystem at all, unlike
// BasicMeleeAISystem/BasicRangedAISystem — a turret's target can never be manually assigned
// by a player.
//
// Targeting priority: an enemy PHYSICAL troop always outranks a neutral-owned building
// (TurretAIComponent.CanTargetNeutralBuildings) — every tick, if the turret isn't currently
// locked onto an enemy troop specifically (whether because it has no target at all, or its
// current lock is a lower-priority neutral building), it checks for the closest enemy troop
// in range and takes that over instead, even overriding an otherwise still-valid building
// lock. A neutral building is only ever engaged as a fallback, when no enemy troop is in
// range at all.
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
    private ComponentStore<BuildingComponent> _buildingStore;

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        _ecs = ecs;
        _posStore = ecs.GetComponentStore<PositionComponent>();
        _troopStore = ecs.GetComponentStore<TroopComponent>();
        _healthStore = ecs.GetComponentStore<HealthComponent>();
        _buildingStore = ecs.GetComponentStore<BuildingComponent>();

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

        // (Re)validate the current lock.
        if (ai.TargetEntityId != 0 && !IsValidTarget(ai.TargetEntityId, troop.OwnerPlayerId, myPos, range, ai.CanTargetNeutralBuildings))
            ai.TargetEntityId = 0;

        // Enemy troops always outrank a neutral building — take one over even while still
        // validly locked onto a (lower-priority) building.
        if (!IsEnemyTroopTarget(ai.TargetEntityId, troop.OwnerPlayerId, myPos, range))
        {
            ulong closestTroop = FindClosestEnemyTroopInRange(id, troop.OwnerPlayerId, myPos, range);
            if (closestTroop != 0)
                ai.TargetEntityId = closestTroop;
        }

        if (ai.TargetEntityId == 0 && ai.CanTargetNeutralBuildings)
            ai.TargetEntityId = FindClosestNeutralBuildingInRange(id, myPos, range);

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
        if (targetId != 0 && IsValidTarget(targetId, ownerPlayerId, myPos, range * ai.AttackRangeMultiplier, ai.CanTargetNeutralBuildings)
            && ActivationQuery.CanPerform(_ecs, id))
        {
            bool fired = ai.ProjectileMode == TurretProjectileMode.Ballistic
                ? FireBallistic(id, myPos, targetId, ai)
                : ProjectilePool.Fire(_ecs, id, targetId, myPos) != 0;

            if (fired)
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

    // Spawns the cosmetic-only flight entity (BallisticProjectileComponent + its own
    // LifetimeComponent, no hitbox) and schedules the actual AOE-on-arrival damage via
    // ScheduledCallSystem for exactly the same duration — see MissileSiloCard.
    // ResolveMissileImpact, registered against ScheduledCallType.MissileImpactResolve.
    private bool FireBallistic(ulong turretId, Vector2 myPos, ulong targetId, TurretAIComponent ai)
    {
        if (!_posStore.HasComponent(targetId)) return false;

        PositionComponent targetPos = _posStore.GetComponent(targetId);
        Vector2 targetVec = new Vector2(targetPos.X, targetPos.Y);

        float distance = Vector2.Distance(myPos, targetVec);
        float speed = Mathf.Max(0.01f, ai.BallisticSpeedTilesPerSecond);
        int flightTicks = Mathf.Max(1, TickManager.SecondsToTicks(distance / speed));

        float range = StatsQuery.GetRange(_ecs, turretId, DefaultRange);
        float impactRadius = range * ai.BallisticImpactRadiusMultiplier;

        EntityHandle missile = _ecs.CreateEntity();
        _ecs.AddComponent(missile.Id, new PositionComponent(myPos.x, myPos.y));
        _ecs.AddComponent(missile.Id, new RenderableComponent { Type = ai.BallisticRenderableType });
        _ecs.AddComponent(missile.Id, new LifetimeComponent
        {
            TicksRemaining        = flightTicks,
            InitialTicksRemaining = flightTicks,
        });
        _ecs.AddComponent(missile.Id, new BallisticProjectileComponent
        {
            TargetX      = targetVec.x,
            TargetY      = targetVec.y,
            ImpactRadius = impactRadius,
        });

        ScheduledCallSystem.Schedule(_ecs, ScheduledCallType.MissileImpactResolve, flightTicks,
            param0: turretId, param1: targetVec.x, param2: targetVec.y);

        return true;
    }

    private bool PassesCommonChecks(ulong targetId, Vector2 myPos, float range)
    {
        if (!_ecs.HasEntity(targetId)) return false;
        if (!_posStore.HasComponent(targetId) || !_troopStore.HasComponent(targetId)) return false;
        if (_troopStore.GetComponent(targetId).IsDead) return false;
        if (_healthStore != null && _healthStore.HasComponent(targetId) && _healthStore.GetComponent(targetId).CurrentHealth <= 0) return false;
        if (!ActivationQuery.IsActivated(_ecs, targetId)) return false;

        PositionComponent targetPos = _posStore.GetComponent(targetId);
        float dx = targetPos.X - myPos.x, dy = targetPos.Y - myPos.y;
        return dx * dx + dy * dy <= range * range;
    }

    private bool IsEnemyTroopTarget(ulong targetId, ushort myOwnerId, Vector2 myPos, float range)
    {
        if (targetId == 0) return false;
        if (!PassesCommonChecks(targetId, myPos, range)) return false;

        TroopComponent target = _troopStore.GetComponent(targetId);
        if (!target.IsPhysicalTroop) return false;
        if (target.OwnerPlayerId == myOwnerId || target.OwnerPlayerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return false;
        return !ShadowCloakSystem.IsCloaked(_ecs, targetId, out _);
    }

    private bool IsNeutralBuildingTarget(ulong targetId, Vector2 myPos, float range)
    {
        if (targetId == 0) return false;
        if (!PassesCommonChecks(targetId, myPos, range)) return false;

        TroopComponent target = _troopStore.GetComponent(targetId);
        if (target.IsPhysicalTroop) return false;
        if (target.OwnerPlayerId != TroopComponent.NEUTRAL_OWNER_PLAYER_ID) return false;
        return _buildingStore != null && _buildingStore.HasComponent(targetId);
    }

    private bool IsValidTarget(ulong targetId, ushort myOwnerId, Vector2 myPos, float range, bool canTargetNeutralBuildings)
        => IsEnemyTroopTarget(targetId, myOwnerId, myPos, range)
        || (canTargetNeutralBuildings && IsNeutralBuildingTarget(targetId, myPos, range));

    private ulong FindClosestEnemyTroopInRange(ulong turretId, ushort myOwnerId, Vector2 myPos, float range)
    {
        _queryBuffer.Clear();
        _ecs.ChunkTracker.GetEntitiesNear(myPos.x, myPos.y, range, _queryBuffer);

        ulong bestId = 0;
        float bestDistSq = float.MaxValue;

        foreach (ulong candidateId in _queryBuffer)
        {
            if (candidateId == turretId) continue;
            if (!IsEnemyTroopTarget(candidateId, myOwnerId, myPos, range)) continue;

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

    private ulong FindClosestNeutralBuildingInRange(ulong turretId, Vector2 myPos, float range)
    {
        _queryBuffer.Clear();
        _ecs.ChunkTracker.GetEntitiesNear(myPos.x, myPos.y, range, _queryBuffer);

        ulong bestId = 0;
        float bestDistSq = float.MaxValue;

        foreach (ulong candidateId in _queryBuffer)
        {
            if (candidateId == turretId) continue;
            if (!IsNeutralBuildingTarget(candidateId, myPos, range)) continue;

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
