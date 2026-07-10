using System.Collections.Generic;

// Server-only. A troop's projectile pool is pre-allocated as ProjectileOwnerComponent.
// MaxProjectiles pooled ProjectileBaseComponent entities that get reused/reset on each
// shot rather than created and destroyed per shot. Once a troop dies, any of its
// projectiles still sitting pooled (i.e. not currently in flight) no longer serve a
// purpose and are torn down here. A projectile that's mid-flight when its owner dies is
// left alone — deleting it out from under a shot already fired would look wrong; it's
// expected to be cleaned up on impact/expiry by whatever system resolves flight instead.
public static class ProjectilePoolCleanupSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static readonly List<ulong> _toDelete = new List<ulong>();

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ComponentStore<ProjectileBaseComponent> projectileStore = ecs.GetComponentStore<ProjectileBaseComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();

        _toDelete.Clear();
        projectileStore.ForEach((ulong id) =>
        {
            if (projectileStore.GetComponent(id).IsActive) return;
            if (!IsOwnerDead(ecs, troopStore, projectileStore.GetComponent(id).OwnerEntityId)) return;
            _toDelete.Add(id);
        });

        foreach (ulong id in _toDelete)
            ecs.DeleteEntity(id);
    }

    // An owner counts as dead once it's fully gone — the normal case, since DeathSystem
    // deletes a dying troop the same tick it sets IsDead — or, defensively, if it's still
    // present but flagged dead.
    private static bool IsOwnerDead(ECS ecs, ComponentStore<TroopComponent> troopStore, ulong ownerEntityId)
    {
        if (!ecs.HasEntity(ownerEntityId)) return true;
        if (!troopStore.HasComponent(ownerEntityId)) return false;
        return troopStore.GetComponent(ownerEntityId).IsDead;
    }
}
