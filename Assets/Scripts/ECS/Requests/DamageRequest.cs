// Deals Amount damage to EntityId's HealthComponent when executed. Cancel() (inherited
// from Request) fully negates the damage — e.g. a shield/invulnerability listener can
// call req.Cancel() from a Subscribe callback before this runs.
public class DamageRequest : Request
{
    public const ulong NO_DEALER_ENTITYID = ulong.MaxValue;
    public ulong DealerEntityId = NO_DEALER_ENTITYID; // optional: the entity that caused this damage, for event listeners to know
    public readonly ulong EntityId;
    public int Amount;

    public DamageRequest(ulong entityId, int amount)
    {
        EntityId = entityId;
        Amount = amount;
    }

    public override void Execute(ECS ecs)
    {
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore == null || !healthStore.HasComponent(EntityId)) return;

        ref HealthComponent health = ref healthStore.GetComponent(EntityId);
        health.CurrentHealth -= Amount;
        ecs.Delta.MarkComponentDirty(EntityId, typeof(HealthComponent));
    }
}
