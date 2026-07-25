// Heals EntityId's HealthComponent by (Additive + Ratio * that entity's own MaxHealth stat)
// when executed, clamped so it never exceeds MaxHealth — the healing mirror of DamageRequest.
// Cancel() (inherited from Request) fully negates the heal, same as DamageRequest.
using UnityEngine;

public class HealRequest : Request
{
    public readonly ulong EntityId;
    public float Additive;
    public float Ratio;

    private const int FallbackMaxHealth = 100;

    public HealRequest(ulong entityId, float additive, float ratio = 0f)
    {
        EntityId = entityId;
        Additive = additive;
        Ratio = ratio;
    }

    public override void Execute(ECS ecs)
    {
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore == null || !healthStore.HasComponent(EntityId)) return;

        int maxHealth = StatsQuery.GetMaxHealth(ecs, EntityId, FallbackMaxHealth);
        int healAmount = Mathf.RoundToInt(Additive + Ratio * maxHealth);
        if (healAmount <= 0) return;

        ref HealthComponent health = ref healthStore.GetComponent(EntityId);
        int newHealth = Mathf.Min(maxHealth, health.CurrentHealth + healAmount);
        if (newHealth == health.CurrentHealth) return;

        int actualHealed = newHealth - health.CurrentHealth;
        health.CurrentHealth = newHealth;
        ecs.Delta.MarkComponentDirty(EntityId, typeof(HealthComponent));

        ecs.FlagEvents.Add(new HealDealtEvent
        {
            EntityId = EntityId,
            Amount = actualHealed,
        });
    }
}
