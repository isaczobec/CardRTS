using System;
using System.Collections.Generic;

// Reacts to activation for any entity carrying a BlinkComponent (see BlinkCard.OnPlayed):
// grants every friendly physical troop (TroopComponent.IsPhysicalTroop — buildings excluded)
// within Range of the spawner's own position a fresh ModifierComponent +
// TeleportingModifierComponent pair targeting it (see TeleportingModifierSystem for what
// actually moves them), then deletes the spawner entity — server-only, mirroring every other
// "do the work unconditionally, delete only on the server" system in this codebase. Granting
// the modifiers is NOT gated behind isServer — like AbilityManager's RingOfProjectilesAbility,
// a client predicting its own copy of these modifiers (under different entity ids than the
// server's) is harmless, since TeleportingModifierSystem only ever acts through
// ModifierComponent.TargetEntityId, never the modifier entity's own identity (see
// ModifierComponent's doc comment).
//
// Polls BlinkComponent.HasFired + ActivationQuery.IsActive every tick (mirroring
// ModifierSystem/TeleportingModifierSystem's own EntityActivatedEvent-free pattern) instead of
// subscribing to EntityActivatedEvent. Two reasons: (1) a FlagEvent subscriber can't safely
// touch the ECS synchronously — FlagEventManager.Flush() enumerates its pending-events list
// and invokes subscribers inline, so raising further FlagEvents (which CreateEntity/
// AddComponent do) from inside a subscriber mutates that same list mid-enumeration and throws
// "Collection was modified" (this is exactly what used to happen here). (2) bridging
// "activated this tick" to "handle it next tick" needs some state to survive the gap, and that
// state has to live on a component (HasFired, here) rather than a system-instance field —
// RunReconciliation's ClientLocalECS.CopyStateFrom(ClientServerMirrorECS) replaces this ECS's
// component-store data wholesale on every reconciliation, but has no way to know about (and so
// cannot roll back) an ordinary C# field living on a system instance, so a system-side queue
// durable across ticks goes stale/desynced the moment a reconciliation rewinds the ECS
// underneath it — this is exactly what used to cause teleported troops to visibly lag back to
// their pre-blink position and then snap forward again.
//
// Instance (not static) and registered per ECS, like TargetingSystem/PathfindingSystem, so a
// client's prediction ECS and the host's authoritative ECS keep independent _queryBuffer/
// _toDelete scratch lists — both purely transient (cleared+filled+drained within a single
// Execute() call, never read across a tick boundary), so they don't carry the same
// reconciliation risk as genuine cross-tick state would.
public class BlinkSystem : ISystem
{
    public Type[] ComponentTypes => Array.Empty<Type>();

    private readonly List<ulong> _queryBuffer = new List<ulong>();
    private readonly List<ulong> _toDelete = new List<ulong>();

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        ComponentStore<BlinkComponent> blinkStore = ecs.GetComponentStore<BlinkComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();

        _toDelete.Clear();

        blinkStore.ForEach((ulong entityId) =>
            ProcessSpawner(ecs, entityId, blinkStore, posStore, troopStore));

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        foreach (ulong id in _toDelete)
            ecs.DeleteEntity(id);
    }

    private void ProcessSpawner(ECS ecs, ulong entityId,
        ComponentStore<BlinkComponent> blinkStore, ComponentStore<PositionComponent> posStore, ComponentStore<TroopComponent> troopStore)
    {
        ref BlinkComponent blink = ref blinkStore.GetComponent(entityId);
        if (blink.HasFired)
        {
            _toDelete.Add(entityId);
            return;
        }

        if (!ActivationQuery.IsActive(ecs, entityId)) return; // still mid-deploy — check again next tick

        if (posStore != null && posStore.HasComponent(entityId) &&
            troopStore != null && troopStore.HasComponent(entityId))
        {
            GrantTeleportModifiers(ecs, entityId, blink, posStore, troopStore);
        }

        blink.HasFired = true;
        ecs.Delta.MarkComponentDirty(entityId, typeof(BlinkComponent));
        _toDelete.Add(entityId);
    }

    private void GrantTeleportModifiers(ECS ecs, ulong entityId, BlinkComponent blink,
        ComponentStore<PositionComponent> posStore, ComponentStore<TroopComponent> troopStore)
    {
        PositionComponent origin = posStore.GetComponent(entityId);
        ushort ownerPlayerId = troopStore.GetComponent(entityId).OwnerPlayerId;

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(origin.X, origin.Y, blink.Range, _queryBuffer);

        foreach (ulong candidateId in _queryBuffer)
        {
            if (candidateId == entityId) continue;
            if (!troopStore.HasComponent(candidateId)) continue;

            TroopComponent troop = troopStore.GetComponent(candidateId);
            if (!troop.IsPhysicalTroop) continue; // buildings excluded
            if (troop.OwnerPlayerId != ownerPlayerId) continue; // friendly only
            if (!posStore.HasComponent(candidateId)) continue;

            // Keep each troop's position relative to the source point on the destination —
            // e.g. a troop standing 2 units east of the source lands 2 units east of the
            // destination — instead of stacking every candidate onto the same exact point.
            PositionComponent candidatePos = posStore.GetComponent(candidateId);
            float offsetX = candidatePos.X - origin.X;
            float offsetY = candidatePos.Y - origin.Y;

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = candidateId,
                TicksRemaining = int.MaxValue,
            });
            ecs.AddComponent(modifier.Id, new TeleportingModifierComponent
            {
                DestinationX = blink.DestinationX + offsetX,
                DestinationY = blink.DestinationY + offsetY,
            });
        }
    }
}
