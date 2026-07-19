using UnityEngine;

// Card kind for "click one ground point, spawn one entity there" cards — today's only
// kind (troops, buildings). See Card.cs for how a future kind (multi-point, entity-
// targeted, ...) would sit alongside this one instead of extending it.
public abstract class SpawnAtPointCard : Card
{
    // Key into IndicatorPrefabRegistry for a prefab that follows the player's mouse
    // (snapped to the ground) while this card is selected/being dragged, previewing where
    // the spawned entity will land — see CardPlacementIndicatorManager. Null/empty means
    // no indicator is shown for this card.
    public virtual string IndicatorPrefabName => null;

    // Called by CardPlacementIndicatorManager right after it instantiates this card's
    // IndicatorPrefabName prefab, with a reference to that instance — so a subtype can
    // customize it (e.g. scaling it to reflect one of its own stats, like AoeSpellCard
    // does with its blast radius). No-op by default; never called for a card whose
    // IndicatorPrefabName is null/empty (nothing gets spawned to pass a reference to).
    public virtual void OnIndicatorSpawned(GameObject indicator) { }

    // Called by SpawnAtPointCardPlaySystem when a player plays this card at (x, y).
    // cardEntityId is the card entity that was played (recycled back into the deck
    // afterwards, not deleted), in case an implementation ever needs to read more off it
    // than SpawnAtPointCardPlaySystem already validated. Returns the id of the entity this
    // play created — SpawnAtPointCardPlaySystem uses it to run any equipped UpgradeComponent's
    // effect (see ECS/Upgrades/CardUpgrade.cs) against the thing that was actually spawned.
    public abstract ulong OnPlayed(ECS ecs, ulong cardEntityId, ushort ownerPlayerId, float x, float y);
}
