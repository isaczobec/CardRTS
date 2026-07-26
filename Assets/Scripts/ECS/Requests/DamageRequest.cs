// Deals Amount damage to EntityId's HealthComponent when executed. Cancel() (inherited
// from Request) fully negates the damage — e.g. a shield/invulnerability listener can
// call req.Cancel() from a Subscribe callback before this runs.
using UnityEngine;

public class DamageRequest : Request
{
    public const ulong NO_DEALER_ENTITYID = ulong.MaxValue;
    public ulong DealerEntityId = NO_DEALER_ENTITYID; // optional: the entity that caused this damage, for event listeners to know
    public readonly ulong EntityId;
    public int Amount;

    // Which stat mitigates this damage (Armor for Normal, SpellResist for Spell) — see
    // ArmorMitigationSystem. Defaults to Normal, so every existing call site that doesn't
    // set this keeps behaving exactly as before DamageType existed.
    public DamageType Type = DamageType.Normal;

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
        health.LastDamageDealer = DealerEntityId;
        ecs.Delta.MarkComponentDirty(EntityId, typeof(HealthComponent));

        PositionQuery.TryGet(ecs, EntityId, out float x, out float y);
        ecs.FlagEvents.Add(new DamageDealtEvent
        {
            EntityId = EntityId,
            DealerEntityId = DealerEntityId,
            Amount = Amount,
            X = x,
            Y = y,
        });
    }
}
