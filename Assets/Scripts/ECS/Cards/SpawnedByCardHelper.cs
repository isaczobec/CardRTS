using System.Collections.Generic;

// Shared helper for tagging/reading SpawnedByCardComponent — see that component's own doc
// comment for what it's used for and where it gets attached.
public static class SpawnedByCardHelper
{
    // No-ops if entityId is already tagged (mirrors the ResourceValueComponent auto-tag
    // pattern in SpawnAtPointCardPlaySystem — a card whose own OnPlayed already tagged its
    // spawn(s) itself, e.g. SkeletonsCard, isn't double-tagged by the play system's generic
    // fallback) or no longer exists (a card's OnPlayed can spawn something with a
    // LifetimeComponent short enough that it's conceivably already gone by the time a
    // caller gets around to tagging it).
    public static void Attach(ECS ecs, ulong entityId, ulong cardEntityId)
    {
        if (cardEntityId == 0 || !ecs.HasEntity(entityId)) return;

        ComponentStore<SpawnedByCardComponent> store = ecs.GetComponentStore<SpawnedByCardComponent>();
        if (store == null || store.HasComponent(entityId)) return;

        ecs.AddComponent(entityId, new SpawnedByCardComponent { CardEntityId = cardEntityId });
    }

    // The card entity id sourceEntityId was itself spawned by, or 0 if it isn't tagged (e.g.
    // it's not a card-spawned troop/building at all). Used to propagate a card instance's
    // identity onto an entity spawned indirectly from an existing troop rather than straight
    // from a card's own OnPlayed — e.g. SkeletonsCard.ResolveSkeletonSummon's kill-triggered
    // resurrection, or SantaClausCard.ResolveSnatcherReinforcement's hit-triggered spawn —
    // where the original played-card entity id isn't otherwise available at that spawn site.
    public static ulong ResolveCardEntityId(ECS ecs, ulong sourceEntityId)
    {
        ComponentStore<SpawnedByCardComponent> store = ecs.GetComponentStore<SpawnedByCardComponent>();
        if (store == null || !store.HasComponent(sourceEntityId)) return 0;
        return store.GetComponent(sourceEntityId).CardEntityId;
    }

    // Every currently-alive entity tagged with CardEntityId == cardEntityId — used by
    // RecallSystem so initiating (or cancelling) a recall for one troop from a multi-spawn
    // card (e.g. one of SkeletonsCard's 8) does the same for every other still-living one
    // from that same played card at once ("pair" troops recall together).
    public static void CollectAliveSpawns(ECS ecs, ulong cardEntityId, List<ulong> results)
    {
        results.Clear();
        if (cardEntityId == 0) return;

        ComponentStore<SpawnedByCardComponent> store = ecs.GetComponentStore<SpawnedByCardComponent>();
        if (store == null) return;

        store.ForEach((ulong id) =>
        {
            if (store.GetComponent(id).CardEntityId == cardEntityId)
                results.Add(id);
        });
    }
}
