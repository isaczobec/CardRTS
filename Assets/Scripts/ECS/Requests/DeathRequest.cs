public class DeathRequest : Request
{
    public readonly ulong EntityId;

    // Whether Execute should delete the entity once it's done marking it dead. Defaults to
    // true; RespawnSystem's pre-execute Subscribe callback sets this to false for
    // respawnable entities, so the rest of Execute (IsDead, TroopDiedEvent, the "executed"
    // notification) still runs as normal but the entity survives to be revived in place
    // instead of being destroyed.
    public bool ShouldDelete = true;

    public DeathRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs)
    {
        var troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(EntityId)) return;

        ref TroopComponent troop = ref troopStore.GetComponent(EntityId);
        troop.IsDead = true;
        ecs.Delta.MarkComponentDirty(EntityId, typeof(TroopComponent));
        ecs.FlagEvents.Add(new TroopDiedEvent { EntityId = EntityId });

        // Notify "executed" subscribers (e.g. OnDeathResourceDropSystem, RespawnSystem)
        // before possibly deleting the entity below — DeleteEntity strips every component
        // off it, so anything that needs to read this entity's HealthComponent/
        // TroopComponent/etc. one last time must run first.
        ecs.NotifyRequestExecuted(this);

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (isServer && ShouldDelete)
            ecs.DeleteEntity(EntityId);
    }
}
