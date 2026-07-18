using System.Collections.Generic;

// Reacts to EntityActivatedEvent for any entity carrying a BlinkComponent (see
// BlinkCard.OnPlayed): grants every friendly physical troop (TroopComponent.IsPhysicalTroop
// — buildings excluded) within Range of the spawner's own position a fresh
// ModifierComponent + TeleportingModifierComponent pair targeting it (see
// TeleportingModifierSystem for what actually moves them), then deletes the spawner entity —
// server-only, mirroring every other "do the work unconditionally, delete only on the
// server" system in this codebase (ModifierSystem, TeleportingModifierSystem, ...). Granting
// the modifiers is NOT gated behind isServer, though — like AbilityManager's
// RingOfProjectilesAbility, a client predicting its own copy of these modifiers (under
// different entity ids than the server's) is harmless, since TeleportingModifierSystem only
// ever acts through ModifierComponent.TargetEntityId, never the modifier entity's own
// identity (see ModifierComponent's doc comment).
//
// Subscribes via ecs.FlagEvents (not TickManager.instance.ServerFlagEvents — this is
// gameplay logic that must run identically on both the client-prediction and server ECS
// instances, unlike a purely-rendering subscriber).
//
// The spawner's own deletion is deliberately NOT done inside OnActivated, even though
// granting modifiers is: ActivationSystem fires EntityActivatedEvent synchronously from
// inside its own ForEach over the ActivatableComponent store (the instant an entity's
// countdown crosses to 0), so a DeleteEntity call made directly from this event handler
// would remove an entry from that SAME store while ActivationSystem is still mid-iteration
// over it — ComponentStore's swap-remove (CopyBackArray.Remove moves the last entry into the
// removed slot) would then silently skip whichever entity got moved into the vacated slot
// for a tick. Granting modifiers is safe to do immediately since it only ever ADDS to
// unrelated component stores (ModifierComponent/TeleportingModifierComponent), never removes
// from the one being iterated. So OnActivated only queues the spawner id; the actual
// DeleteEntity happens in Execute, which runs at this system's own place in the tick order —
// strictly after ActivationSystem's own Execute has already finished iterating for that
// tick — so it's still deleted the same tick it activated, just safely.
public static class BlinkSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static readonly List<ulong> _queryBuffer = new List<ulong>();
    private static readonly List<ulong> _activatedThisTick = new List<ulong>();

    private static void Setup(ECS ecs)
    {
        ecs.FlagEvents.Subscribe<EntityActivatedEvent>(e => OnActivated(ecs, e));
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        if (_activatedThisTick.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (isServer)
        {
            foreach (ulong id in _activatedThisTick)
                ecs.DeleteEntity(id);
        }

        _activatedThisTick.Clear();
    }

    private static void OnActivated(ECS ecs, EntityActivatedEvent e)
    {
        ComponentStore<BlinkComponent> blinkStore = ecs.GetComponentStore<BlinkComponent>();
        if (blinkStore == null || !blinkStore.HasComponent(e.EntityId)) return;

        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (posStore == null || !posStore.HasComponent(e.EntityId)) return;
        if (troopStore == null || !troopStore.HasComponent(e.EntityId)) return;

        BlinkComponent blink = blinkStore.GetComponent(e.EntityId);
        PositionComponent origin = posStore.GetComponent(e.EntityId);
        ushort ownerPlayerId = troopStore.GetComponent(e.EntityId).OwnerPlayerId;

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(origin.X, origin.Y, blink.Range, _queryBuffer);

        foreach (ulong candidateId in _queryBuffer)
        {
            if (candidateId == e.EntityId) continue;
            if (!troopStore.HasComponent(candidateId)) continue;

            TroopComponent troop = troopStore.GetComponent(candidateId);
            if (!troop.IsPhysicalTroop) continue; // buildings excluded
            if (troop.OwnerPlayerId != ownerPlayerId) continue; // friendly only

            EntityHandle modifier = ecs.CreateEntity();
            ecs.AddComponent(modifier.Id, new ModifierComponent
            {
                TargetEntityId = candidateId,
                TicksRemaining = int.MaxValue,
            });
            ecs.AddComponent(modifier.Id, new TeleportingModifierComponent
            {
                DestinationX = blink.DestinationX,
                DestinationY = blink.DestinationY,
            });
        }

        _activatedThisTick.Add(e.EntityId);
    }
}
