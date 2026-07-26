// Starts a respawnable entity's cooldown countdown. Factored out of RespawnSystem's
// SubscribeExecuted<DeathRequest> callback so that "an entity begins its respawn cooldown" is
// its own explicit, named action (Requests.Process runs Execute synchronously, so this fires
// immediately from within that same callback, right after DeathRequest itself finished
// executing).
public class RespawnRequest : Request
{
    public readonly ulong EntityId;

    public RespawnRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs)
    {
        var respawnStore = ecs.GetComponentStore<RespawnableInPlaceComponent>();
        if (respawnStore == null || !respawnStore.HasComponent(EntityId)) return;

        ref RespawnableInPlaceComponent respawn = ref respawnStore.GetComponent(EntityId);
        respawn.TicksUntilRespawn = respawn.CooldownTicks;
        ecs.Delta.MarkComponentDirty(EntityId, typeof(RespawnableInPlaceComponent));

        PositionQuery.TryGet(ecs, EntityId, out float x, out float y);
        ecs.FlagEvents.Add(new RespawnableEntityDiedEvent { EntityId = EntityId, X = x, Y = y });
    }
}
