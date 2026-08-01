using System.Collections.Generic;
using UnityEngine;

// Drives every TornadoProjectileComponent entity (TornadoCard). Gated on
// ActivationQuery.IsActivated — before the entity's own ActivatableComponent delay finishes,
// this does nothing at all, so the tornado just sits at its cast point as a telegraph; once
// activated, each tick it steps its own PositionComponent a further TilesPerSecond toward
// Destination(X/Y) (landing exactly on it rather than overshooting, on whichever tick would
// otherwise pass it), then pulls along every enemy troop currently overlapping HitRadius of
// its (new) position.
//
// The pull reuses PirateCard's own Hook primitive (DisplacementSystem.BeginDisplacement —
// see ProjectileOnHitSystem.ApplyHook) exactly, just re-issued every tick an enemy remains
// inside HitRadius instead of once on a single hit: each call sets the target's displacement
// velocity to the tornado's OWN current travel velocity, for a couple of ticks. Re-triggered
// every tick this way, a caught enemy keeps pace with the tornado for as long as it stays
// inside — "brought along" toward the destination — and coasts to a stop within a tick or
// two of falling outside HitRadius (e.g. once the tornado has swept past it) rather than
// continuing under its own momentum.
//
// Registered as a GlobalSystem (no per-instance scratch beyond the reused query buffer,
// mirroring SkillshotProjectileSystem) BEFORE DisplacementSystem in TickManager's system
// order, so a pull issued this tick is already applied by DisplacementSystem's own Execute
// later this same tick instead of lagging a tick behind. Movement and pulls both run
// unconditionally (predicted on clients too — mutating PositionComponent/MovableComponent is
// safe to predict, same reasoning as DisplacementSystem's own doc comment); this system never
// deletes anything (LifetimeComponent's own natural expiry — sized in TornadoCard.OnPlayed to
// cover the full activation-delay-then-sweep duration — is what removes the entity), so there
// is no isServer-gated step here either.
public static class TornadoProjectileSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    // How many ticks a single pull refresh lasts once issued — kept just long enough to
    // bridge to the next tick's refresh while the target is still inside HitRadius, so the
    // pull dies out almost immediately once the tornado has actually passed over it.
    private const int PullRefreshTicks = 2;

    private static readonly List<ulong> _queryBuffer = new List<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<TornadoProjectileComponent> tornadoStore = ecs.GetComponentStore<TornadoProjectileComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        if (tornadoStore == null || posStore == null) return;

        tornadoStore.ForEach((ulong id) => Tick(id, ecs, tornadoStore, posStore));
    }

    private static void Tick(ulong id, ECS ecs,
        ComponentStore<TornadoProjectileComponent> tornadoStore,
        ComponentStore<PositionComponent> posStore)
    {
        if (!posStore.HasComponent(id)) return;
        if (!ActivationQuery.IsActivated(ecs, id)) return; // still winding up — no movement, no pull yet

        TornadoProjectileComponent tornado = tornadoStore.GetComponent(id);
        ref PositionComponent pos = ref posStore.GetComponent(id);

        Vector2 current = new Vector2(pos.X, pos.Y);
        Vector2 destination = new Vector2(tornado.DestinationX, tornado.DestinationY);
        Vector2 toDestination = destination - current;

        float step = Mathf.Max(0f, tornado.TilesPerSecond) * TickManager.TicksToSeconds(1f);
        Vector2 velocity;

        if (toDestination.sqrMagnitude <= step * step)
        {
            // Last step — land exactly on the destination instead of overshooting past it,
            // same reasoning as DisplacementSystem stopping a knockback right where it lands.
            velocity = toDestination.sqrMagnitude > 0.0001f ? toDestination.normalized * tornado.TilesPerSecond : Vector2.zero;
            pos.X = destination.x;
            pos.Y = destination.y;
        }
        else
        {
            Vector2 direction = toDestination.normalized;
            velocity = direction * tornado.TilesPerSecond;
            Vector2 next = current + direction * step;
            pos.X = next.x;
            pos.Y = next.y;
        }
        ecs.Delta.MarkComponentDirty(id, typeof(PositionComponent));

        PullOverlappingEnemies(ecs, tornado.OwnerPlayerId, new Vector2(pos.X, pos.Y), tornado.HitRadius, velocity);
    }

    private static void PullOverlappingEnemies(ECS ecs, ushort ownerPlayerId, Vector2 pos, float hitRadius, Vector2 velocity)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        ComponentStore<MovableComponent> movStore = ecs.GetComponentStore<MovableComponent>();
        if (troopStore == null || movStore == null) return;

        _queryBuffer.Clear();
        ecs.ChunkTracker.GetEntitiesNear(pos.x, pos.y, hitRadius, _queryBuffer);

        foreach (ulong targetId in _queryBuffer)
        {
            if (!troopStore.HasComponent(targetId)) continue;

            TroopComponent target = troopStore.GetComponent(targetId);
            if (target.OwnerPlayerId == ownerPlayerId) continue; // never friendly fire
            if (target.IsDead) continue;
            if (!movStore.HasComponent(targetId)) continue; // nothing to displace (e.g. a building)
            if (!ActivationQuery.IsActivated(ecs, targetId)) continue;

            DisplacementSystem.BeginDisplacement(ecs, targetId, velocity.x, velocity.y, PullRefreshTicks);
        }
    }
}
