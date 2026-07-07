using System;

// Reads StatsComponent fields for an entity, falling back to a caller-supplied default
// when the entity has no StatsComponent at all.
public static class StatsQuery
{
    public static int GetMaxHealth(ECS ecs, ulong entityId, int defaultValue) => Get(ecs, entityId, defaultValue, s => s.MaxHealth);
    public static int GetSpeed(ECS ecs, ulong entityId, int defaultValue) => Get(ecs, entityId, defaultValue, s => s.Speed);
    public static int GetRange(ECS ecs, ulong entityId, int defaultValue) => Get(ecs, entityId, defaultValue, s => s.Range);
    public static int GetArmor(ECS ecs, ulong entityId, int defaultValue) => Get(ecs, entityId, defaultValue, s => s.Armor);
    public static int GetDamage(ECS ecs, ulong entityId, int defaultValue) => Get(ecs, entityId, defaultValue, s => s.Damage);

    private static int Get(ECS ecs, ulong entityId, int defaultValue, Func<StatsComponent, int> selector)
    {
        ComponentStore<StatsComponent> store = ecs.GetComponentStore<StatsComponent>();
        if (store == null || !store.HasComponent(entityId)) return defaultValue;
        return selector(store.GetComponent(entityId));
    }
}
