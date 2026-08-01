using UnityEngine;

// Shared helper for attaching/deriving ResourceValueComponent values — see that component's
// own doc comment for what it's used for and where it gets attached.
public static class ResourceValueHelper
{
    public static void Attach(ECS ecs, ulong entityId, ushort ownerPlayerId, ResourceCost value)
    {
        ecs.AddComponent(entityId, new ResourceValueComponent
        {
            OwnerPlayerId       = ownerPlayerId,
            Wood                = value.Wood,
            Stone               = value.Stone,
            Metal               = value.Metal,
            Gems                = value.Gems,
            Soulstones          = value.Soulstones,
            Gold                = value.Gold,
            CanCollectResources = CanEntityCollectResources(ecs, entityId),
        });
    }

    // See ResourceValueComponent.CanCollectResources's own doc comment. Computed here (once,
    // at attach time) rather than re-derived on every read — every troop that will ever get
    // a LifetimeComponent already has it by the time Attach runs (added earlier in the same
    // spawn call, before the automatic SpawnAtPointCardPlaySystem tag-on-play or a card's own
    // manual Attach call — see each's own comment), and nothing in this codebase adds a
    // LifetimeComponent to an entity after the fact, so the flag stays accurate for the
    // entity's whole life.
    private static bool CanEntityCollectResources(ECS ecs, ulong entityId)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(entityId)) return false;
        if (!troopStore.GetComponent(entityId).IsPhysicalTroop) return false;

        ComponentStore<LifetimeComponent> lifetimeStore = ecs.GetComponentStore<LifetimeComponent>();
        return lifetimeStore == null || !lifetimeStore.HasComponent(entityId);
    }

    // Multiplies every field by ratio (rounded) — e.g. a weaker/temporary reinforcement
    // spawned mid-fight rather than bought outright (see SantaClausCard.
    // ResolveSnatcherReinforcement) is valued as a fraction of the card it's thematically
    // equivalent to.
    public static ResourceCost Scale(ResourceCost cost, float ratio) => new ResourceCost
    {
        Wood       = Mathf.RoundToInt(cost.Wood       * ratio),
        Stone      = Mathf.RoundToInt(cost.Stone      * ratio),
        Metal      = Mathf.RoundToInt(cost.Metal      * ratio),
        Gems       = Mathf.RoundToInt(cost.Gems       * ratio),
        Soulstones = Mathf.RoundToInt(cost.Soulstones * ratio),
        Gold       = Mathf.RoundToInt(cost.Gold       * ratio),
    };

    // Splits cost evenly across spawnCount entities — used by cards that spawn several
    // troops from a single play (e.g. SkeletonsCard's ring of 8).
    public static ResourceCost Split(ResourceCost cost, int spawnCount)
        => spawnCount <= 1 ? cost : Scale(cost, 1f / spawnCount);
}
