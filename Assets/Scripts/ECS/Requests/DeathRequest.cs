public class DeathRequest : Request
{
    public readonly ulong EntityId;

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

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (isServer)
            ecs.DeleteEntity(EntityId);
    }
}
