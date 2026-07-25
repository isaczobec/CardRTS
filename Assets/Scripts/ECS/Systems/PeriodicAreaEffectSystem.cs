using System;
using System.Collections.Generic;

// Drives every PeriodicAreaEffectComponent: every PeriodTicks ticks, resolves this entity's
// own scan "source" (see ResolveSource), scans ChunkTracker for everything within
// RangeMultiplier x the source's own Range stat, filters candidates down to whichever
// relationship(s) Targets allows (friendly/enemy/neutral, relative to the source's own
// OwnerPlayerId), then invokes whichever function is registered under EffectType (see
// RegisterEffect) once per qualifying entity. A pure reusable scan-and-dispatch primitive —
// mirrors ScheduledCallSystem's own enum-to-Action registry idiom, generalizing
// DamageAuraSystem's hardcoded "damage everything in range" pulse into "invoke a pluggable
// effect against a filtered set of nearby entities" so future area effects (e.g. Healer
// Guardian's heal aura) can reuse the scan/filter logic instead of duplicating it.
public static class PeriodicAreaEffectSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const int DefaultRange = 5;

    private static readonly Dictionary<AreaEffectType, Action<ECS, ulong, ulong, PeriodicAreaEffectComponent>> _effects
        = new Dictionary<AreaEffectType, Action<ECS, ulong, ulong, PeriodicAreaEffectComponent>>();

    // Associates an AreaEffectType with the function it should invoke against each qualifying
    // nearby entity found each proc — (ecs, sourceId, targetId, component). Called once, at
    // initialization time, by whichever class statically declares the function (e.g.
    // HealerGuardianCard's static constructor for HealPulse).
    public static void RegisterEffect(AreaEffectType type, Action<ECS, ulong, ulong, PeriodicAreaEffectComponent> effect)
        => _effects[type] = effect;

    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<PeriodicAreaEffectComponent> store = ecs.GetComponentStore<PeriodicAreaEffectComponent>();
        if (store == null) return;

        store.ForEach((ulong id) => Tick(ecs, id, store));
    }

    private static void Tick(ECS ecs, ulong id, ComponentStore<PeriodicAreaEffectComponent> store)
    {
        if (!IsEffectActive(ecs, id)) return;

        ref PeriodicAreaEffectComponent effect = ref store.GetComponent(id);

        effect.TicksUntilNextProc--;
        if (effect.TicksUntilNextProc > 0)
        {
            ecs.Delta.MarkComponentDirty(id, typeof(PeriodicAreaEffectComponent));
            return;
        }

        effect.TicksUntilNextProc = Math.Max(1, effect.PeriodTicks);
        ecs.Delta.MarkComponentDirty(id, typeof(PeriodicAreaEffectComponent));

        if (ResolveSource(ecs, id, out ulong sourceId))
            Pulse(ecs, sourceId, effect);
    }

    // An entity paired with a ModifierComponent (Healer Guardian-style) gates on
    // ModifierQuery.IsActive, matching every other modifier payload in this codebase; one
    // living directly on a permanent troop/building (DamageAuraComponent-style) gates on its
    // own ActivationQuery instead, matching DamageAuraSystem.
    private static bool IsEffectActive(ECS ecs, ulong id)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore != null && modifierStore.HasComponent(id))
            return ModifierQuery.IsActive(ecs, id);

        return ActivationQuery.IsActivated(ecs, id) && ActivationQuery.CanPerform(ecs, id);
    }

    private static bool ResolveSource(ECS ecs, ulong id, out ulong sourceId)
    {
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (posStore != null && posStore.HasComponent(id))
        {
            sourceId = id;
            return true;
        }

        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore != null && modifierStore.HasComponent(id))
        {
            sourceId = modifierStore.GetComponent(id).TargetEntityId;
            return true;
        }

        sourceId = 0;
        return false;
    }

    private static void Pulse(ECS ecs, ulong sourceId, PeriodicAreaEffectComponent effect)
    {
        if (!_effects.TryGetValue(effect.EffectType, out Action<ECS, ulong, ulong, PeriodicAreaEffectComponent> action))
            return;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (posStore == null || troopStore == null) return;
        if (!posStore.HasComponent(sourceId) || !troopStore.HasComponent(sourceId)) return;

        PositionComponent pos = posStore.GetComponent(sourceId);
        ushort sourceOwnerId = troopStore.GetComponent(sourceId).OwnerPlayerId;
        float range = StatsQuery.GetRange(ecs, sourceId, DefaultRange) * effect.RangeMultiplier;

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(pos.X, pos.Y, range, _queryBuffer);

        foreach (ulong targetId in _queryBuffer)
        {
            if (targetId == sourceId) continue;
            if (!troopStore.HasComponent(targetId)) continue;

            TroopComponent target = troopStore.GetComponent(targetId);
            if (!target.IsPhysicalTroop) continue;
            if (target.IsDead) continue;
            if (!MatchesTargetFlags(sourceOwnerId, target.OwnerPlayerId, effect.Targets)) continue;

            action(ecs, sourceId, targetId, effect);
        }
    }

    private static bool MatchesTargetFlags(ushort sourceOwnerId, ushort candidateOwnerId, AreaTargetFlags flags)
    {
        if (candidateOwnerId == TroopComponent.NEUTRAL_OWNER_PLAYER_ID)
            return (flags & AreaTargetFlags.Neutral) != 0;
        if (candidateOwnerId == sourceOwnerId)
            return (flags & AreaTargetFlags.Friendly) != 0;
        return (flags & AreaTargetFlags.Enemy) != 0;
    }
}
