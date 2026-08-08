using System.Collections.Generic;

/// <summary>
/// Server-only. Reads SpawnAtPointInput each tick: validates the card entity exists,
/// belongs to the requesting client, is currently in hand, and is actually a
/// SpawnAtPointCard (see Card.cs), then dispatches to the matching Card definition's
/// OnPlayed. The card entity isn't deleted — it's recycled back into its owner's deck (at
/// the back, via DeckHelper) so DeckSystem eventually draws it again.
///
/// This is one of potentially several per-card-kind play systems (each pairing one
/// InputBase subtype with one Card subtype) — see Card.cs for why OnPlayed lives on
/// SpawnAtPointCard rather than Card itself.
/// </summary>
public static class SpawnAtPointCardPlaySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<SpawnAtPointInput> inputs = ecs.GetInputsForTick<SpawnAtPointInput>();
        if (inputs == null || inputs.Count == 0) return;

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();

        foreach (SpawnAtPointInput input in inputs)
            PlayCard(ecs, input, cardStore);
    }

    private static void PlayCard(ECS ecs, SpawnAtPointInput input, ComponentStore<CardComponent> cardStore)
    {
        if (!ecs.HasEntity(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: entity {input.CardEntityId} does not exist (client {input.ClientId}).", "cards");
            return;
        }
        if (!cardStore.HasComponent(input.CardEntityId))
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: entity {input.CardEntityId} has no CardComponent (client {input.ClientId}).", "cards");
            return;
        }

        CardComponent card = cardStore.GetComponent(input.CardEntityId);
        if (card.OwnerPlayerId != input.ClientId)
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: card {input.CardEntityId} owner {card.OwnerPlayerId} != requesting client {input.ClientId}.", "cards");
            return;
        }
        if (card.Location != CardLocation.Hand)
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: card {input.CardEntityId} is in {card.Location}, not Hand (client {input.ClientId}).", "cards");
            return;
        }

        if (!CardRegistry.TryGet(card.Type, out Card definition))
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: card type {card.Type} not found in CardRegistry (client {input.ClientId}).", "cards");
            return;
        }

        // A SpawnAtPointInput for a card that isn't actually a SpawnAtPointCard (e.g. a
        // future multi-point/entity-targeted CardType) is never legal — that card kind has
        // its own InputBase subtype and play system instead.
        if (definition is not SpawnAtPointCard spawnAtPointCard)
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: card type {card.Type} is not a SpawnAtPointCard (client {input.ClientId}).", "cards");
            return;
        }

        // Nothing may be spawned onto non-walkable ground (water/mountain/Air/etc. — see
        // WorldManager's Tile Settings) — reuses the same navmesh lookup pathfinding itself
        // is built from, rather than a separate tile-collision check, so "spawnable" and
        // "reachable" can never disagree.
        if (NavMeshHandler.instance != null && NavMeshHandler.instance.GetNodeAtWorldCoords(input.X, input.Y) == null)
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: ({input.X}, {input.Y}) is not on walkable ground (client {input.ClientId}).", "cards");
            return;
        }

        if (definition.RequiresFriendlyBuildingRange())
        {
            bool inBuildingRange = BuildingRangeHelper.IsWithinRangeOfFriendlyBuilding(ecs, card.OwnerPlayerId, input.X, input.Y, definition.MaxDistanceFromFriendlyBuilding);
            bool inTroopRange = definition.AllowsFriendlyTroopRange() &&
                TroopRangeHelper.IsWithinRangeOfFriendlyTroop(ecs, card.OwnerPlayerId, input.X, input.Y, definition.MaxDistanceFromFriendlyTroop);

            if (!inBuildingRange && !inTroopRange)
            {
                DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: ({input.X}, {input.Y}) is not within range of any friendly building or troop for player {card.OwnerPlayerId} (building range {definition.MaxDistanceFromFriendlyBuilding}, troop range {definition.MaxDistanceFromFriendlyTroop}).", "cards");
                return;
            }
        }

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, card.OwnerPlayerId);
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId))
        {
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: no PlayerResourcesComponent found for player {card.OwnerPlayerId} (resourceEntityId={resourceEntityId}, client {input.ClientId}).", "cards");
            return;
        }
        if (!definition.Cost.CanAfford(resourceStore.GetComponent(resourceEntityId)))
        {
            PlayerResourcesComponent res = resourceStore.GetComponent(resourceEntityId);
            DebugLogger.LogWarning($"[SpawnAtPointCardPlaySystem] Rejected: player {card.OwnerPlayerId} can't afford {card.Type} (cost W{definition.Cost.Wood}/S{definition.Cost.Stone}/M{definition.Cost.Metal}/G{definition.Cost.Gems}/So{definition.Cost.Soulstones}/Au{definition.Cost.Gold} vs have W{res.WoodFloor}/S{res.StoneFloor}/M{res.MetalFloor}/G{res.GemsFloor}/So{res.SoulstonesFloor}/Au{res.GoldFloor}).", "cards");
            return;
        }

        ecs.Requests.Process(new ResourcesDeductedRequest(resourceEntityId, definition.Cost), ecs);

        ulong spawnedEntityId = spawnAtPointCard.OnPlayed(ecs, input.CardEntityId, input.ClientId, input.X, input.Y);
        ecs.FlagEvents.Add(new CardPlayedEvent { EntityId = input.CardEntityId });

        // Run every upgrade equipped on this card (see ECS/Upgrades/UpgradeComponent.cs — a
        // card can carry any number of upgrade entities) against the entity this play just
        // spawned. This whole system is server-only (see the isServer check in Execute
        // above), matching the upgrade lambda's own "runs on the server" contract.
        UpgradeQuery.ForEachUpgradeOnCard(ecs, input.CardEntityId, upgrade =>
            upgrade.OnSpawnAtPointCardPlayed?.Invoke(spawnedEntityId, ecs));

        // A just-applied upgrade (e.g. HealthBonusUpgrade) can raise this entity's effective
        // max health via a StatModifierComponent — but TroopCardHelper/BuildingSpawnHelper
        // already set CurrentHealth to the pre-upgrade base MaxHealth before any of the loop
        // above ran (upgrades are applied to the entity AFTER it's spawned, not before), so
        // without this the entity would spawn missing exactly the upgrade's bonus instead of
        // at full health. Re-synced here, once, after every upgrade has had a chance to
        // modify max health.
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore != null && healthStore.HasComponent(spawnedEntityId))
        {
            ref HealthComponent health = ref healthStore.GetComponent(spawnedEntityId);
            health.CurrentHealth = StatsQuery.GetMaxHealth(ecs, spawnedEntityId, health.CurrentHealth);
            ecs.Delta.MarkComponentDirty(spawnedEntityId, typeof(HealthComponent));
        }

        // Tag the spawned entity with its own resource value (see ResourceValueComponent),
        // valued at the full cost of the card that spawned it — skipped if OnPlayed already
        // tagged it itself, which is how cards that spawn SEVERAL entities from one play
        // (e.g. SkeletonsCard) get each one valued at an even split of the cost instead of
        // the full amount N times over.
        ComponentStore<ResourceValueComponent> resourceValueStore = ecs.GetComponentStore<ResourceValueComponent>();
        if (resourceValueStore != null && ecs.HasEntity(spawnedEntityId) && !resourceValueStore.HasComponent(spawnedEntityId))
            ResourceValueHelper.Attach(ecs, spawnedEntityId, card.OwnerPlayerId, definition.Cost);

        // Construction Worker's own aura — no-ops unless spawnedEntityId is actually a
        // building and a qualifying worker is nearby (see BuildingRefundHelper). Placed after
        // the ResourceValueComponent tagging above since it reads that component.
        if (ecs.HasEntity(spawnedEntityId))
            BuildingRefundHelper.TryRefund(ecs, spawnedEntityId, card.OwnerPlayerId);

        // Recycle the card back into its owner's deck (at the back) rather than
        // deleting it.
        ref CardComponent playedCard = ref cardStore.GetComponent(input.CardEntityId);
        playedCard.Location = CardLocation.Deck;
        ecs.Delta.MarkComponentDirty(input.CardEntityId, typeof(CardComponent));

        DeckHelper.EnqueueToDeck(ecs, card.OwnerPlayerId, input.CardEntityId);
    }
}
