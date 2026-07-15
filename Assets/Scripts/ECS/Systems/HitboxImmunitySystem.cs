// Counts every HealthComponent's HitboxImmunityTicksRemaining down by one tick (floor at
// 0). Kept separate from whatever sets that field (e.g. SkillshotProjectileSystem) so any
// future hit-granting source only has to set the field, not remember to also decay it.
public static class HitboxImmunitySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();

        healthStore.ForEach((ulong id) =>
        {
            ref HealthComponent health = ref healthStore.GetComponent(id);
            if (health.HitboxImmunityTicksRemaining <= 0) return;

            health.HitboxImmunityTicksRemaining--;
            ecs.Delta.MarkComponentDirty(id, typeof(HealthComponent));
        });
    }
}
