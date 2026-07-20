using System.Collections.Generic;
using UnityEngine;

// Reads the position, Range, AttackSpeed, and Damage stats of every DamageAuraComponent
// entity and periodically deals Damage to every entity within Range * RangeMultiplier — no
// owner/friendly-fire filtering, so a pulse hits everything in range including its own
// owner if applicable; add filtering here if a specific spell needs it. The pulse interval
// is StatsComponent.AttackSpeed * AttackSpeedMultiplier ticks, the same ticks-based unit
// every other card's AttackSpeed already uses (see StatsQuery/
// TickManager.MillisecondsToTicks). Only pulses while the entity IsActivated (see
// ActivationQuery) — an aura entity that's still mid-deploy, or past its LifetimeComponent
// countdown, deals no damage — and CanPerform, same as an attack/ability (e.g. a silence
// stops the pulse without needing its own bespoke veto).
public static class DamageAuraSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private const int DefaultRange = 5;
    private const float DefaultAttackSpeedMilliseconds = 1000f;
    private const int DefaultDamage = 10;

    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<DamageAuraComponent> auraStore = ecs.GetComponentStore<DamageAuraComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();

        auraStore.ForEach((ulong id) => Tick(ecs, id, auraStore, posStore));
    }

    private static void Tick(ECS ecs, ulong id, ComponentStore<DamageAuraComponent> auraStore, ComponentStore<PositionComponent> posStore)
    {
        if (!posStore.HasComponent(id)) return;
        if (!ActivationQuery.IsActivated(ecs, id)) return;
        if (!ActivationQuery.CanPerform(ecs, id)) return;

        ref DamageAuraComponent aura = ref auraStore.GetComponent(id);

        if (aura.TicksUntilNextPulse > 0)
        {
            aura.TicksUntilNextPulse--;
            ecs.Delta.MarkComponentDirty(id, typeof(DamageAuraComponent));
            return;
        }

        Pulse(ecs, id, aura, posStore.GetComponent(id));

        int attackSpeedTicks = StatsQuery.GetAttackSpeed(ecs, id, TickManager.MillisecondsToTicks(DefaultAttackSpeedMilliseconds));
        aura.TicksUntilNextPulse = Mathf.RoundToInt(attackSpeedTicks * aura.AttackSpeedMultiplier);
        ecs.Delta.MarkComponentDirty(id, typeof(DamageAuraComponent));
    }

    private static void Pulse(ECS ecs, ulong id, DamageAuraComponent aura, PositionComponent pos)
    {
        float range = StatsQuery.GetRange(ecs, id, DefaultRange) * aura.RangeMultiplier;
        int damage = StatsQuery.GetDamage(ecs, id, DefaultDamage);

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(pos.X, pos.Y, range, _queryBuffer);

        foreach (ulong targetId in _queryBuffer)
        {
            if (targetId == id) continue;
            ecs.Requests.CreateRequest(new DamageRequest(targetId, damage) { DealerEntityId = id, Type = aura.DamageType });
        }
    }
}
