using System;
using UnityEngine;

// Reads StatsComponent fields for an entity, falling back to a caller-supplied default when
// the entity has no StatsComponent at all, then runs the matching StatRequest (see
// StatRequest.cs) so any StatModifierComponent-carrying modifier entity targeting this one
// (see StatModifierSystem) can contribute a ratio multiplier and/or additive bonus.
// STAT_NA is passed straight through, bypassing modifiers entirely — a stat that doesn't
// apply to this entity at all (e.g. Speed on a building) should never come out as some
// other numeric value just because a modifier happens to be targeting it.
public static class StatsQuery
{
    public static int GetMaxHealth(ECS ecs, ulong entityId, int defaultValue) => Resolve<GetMaxHealthRequest>(ecs, entityId, defaultValue, s => s.MaxHealth);
    public static int GetSpeed(ECS ecs, ulong entityId, int defaultValue) => Resolve<GetSpeedRequest>(ecs, entityId, defaultValue, s => s.Speed);
    public static int GetRange(ECS ecs, ulong entityId, int defaultValue) => Resolve<GetRangeRequest>(ecs, entityId, defaultValue, s => s.Range);
    public static int GetArmor(ECS ecs, ulong entityId, int defaultValue) => Resolve<GetArmorRequest>(ecs, entityId, defaultValue, s => s.Armor);
    public static int GetDamage(ECS ecs, ulong entityId, int defaultValue) => Resolve<GetDamageStatRequest>(ecs, entityId, defaultValue, s => s.Damage);
    public static int GetAttackSpeed(ECS ecs, ulong entityId, int defaultValue) => Resolve<GetAttackSpeedRequest>(ecs, entityId, defaultValue, s => s.AttackSpeed);

    private static int Resolve<T>(ECS ecs, ulong entityId, int defaultValue, Func<StatsComponent, int> selector) where T : StatRequest, new()
    {
        ComponentStore<StatsComponent> store = ecs.GetComponentStore<StatsComponent>();
        int baseValue = (store != null && store.HasComponent(entityId)) ? selector(store.GetComponent(entityId)) : defaultValue;

        if (baseValue == StatsComponent.STAT_NA) return StatsComponent.STAT_NA;

        T request = new T { EntityId = entityId, BaseValue = baseValue };
        ecs.Requests.Process(request, ecs, executeIfNotCancelled: false);

        return Mathf.RoundToInt(request.BaseValue * request.RatioMultiplier + request.AdditiveBonus);
    }
}
