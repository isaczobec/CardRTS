using System.Collections.Generic;

/// <summary>
/// Server-only. Reads SpawnAtPointInput each tick: validates the card entity exists,
/// belongs to the requesting client, is currently in hand, and is actually a
/// SpawnAtPointCard (see Card.cs), then dispatches to the matching Card definition's
/// OnPlayed. The card entity isn't deleted — see CardReturnHelper for what happens to it
/// next: a Spell card is recycled straight back into its owner's deck (at the back, via
/// DeckHelper) so DeckSystem eventually draws it again, while a Troop/Building card is held
/// out of the deck/hand rotation until every troop/building it spawned has died (see
/// SpawnedByCardComponent/CardReturnSystem).
///
/// This is one of potentially several per-card-kind play systems (each pairing one
/// InputBase subtype with one Card subtype) — see Card.cs for why OnPlayed lives on
/// SpawnAtPointCard rather than Card itself.
/// </summary>
public static class SpawnAtPointCardPlaySystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    // Scratch buffer for every entity a single play actually spawned (see PlayCard) — reused
    // across calls rather than allocated per call, same "static shared scratch list" shape
    // RecallSystem's own _groupScratch uses.
    private static readonly List<ulong> _spawnGroupScratch = new List<ulong>();

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

        // Tag the spawned entity as belonging to this played card instance (see
        // SpawnedByCardComponent) — only for a Troop/Building card, and only if OnPlayed
        // didn't already tag it (or several others) itself. Done BEFORE the upgrade loop
        // below (unlike where this used to sit, after it) so a card whose OnPlayed spawns
        // SEVERAL entities from one play (e.g. SkeletonsCard's 8, each already tagged inside
        // its own spawn helper) is fully tagged in time for the CollectAliveSpawns call just
        // below to find every one of them, not just spawnedEntityId itself. A Spell card's
        // spawn (if any) is never tracked this way — see CardReturnHelper.
        if ((definition.Category == CardCategory.Troop || definition.Category == CardCategory.Building) && ecs.HasEntity(spawnedEntityId))
            SpawnedByCardHelper.Attach(ecs, spawnedEntityId, input.CardEntityId);

        // Every entity this play actually spawned — usually just spawnedEntityId, but a card
        // whose OnPlayed spawns several entities from one play (e.g. SkeletonsCard/OrcsCard/
        // EphemeralSkeletonsCard) needs every one of them here, not just the single id
        // OnPlayed happened to return (see SpawnAtPointCard.OnPlayed's own doc comment on why
        // it can only report one). Falls back to spawnedEntityId alone when nothing got
        // tagged above (e.g. a Spell card).
        SpawnedByCardHelper.CollectAliveSpawns(ecs, input.CardEntityId, _spawnGroupScratch);
        if (_spawnGroupScratch.Count == 0)
            _spawnGroupScratch.Add(spawnedEntityId);

        // Run every upgrade equipped on this card (see ECS/Upgrades/UpgradeComponent.cs — a
        // card can carry any number of upgrade entities) against EVERY entity this play just
        // spawned, not just one — see _spawnGroupScratch above. This whole system is
        // server-only (see the isServer check in Execute above), matching the upgrade
        // lambda's own "runs on the server" contract.
        UpgradeQuery.ForEachUpgradeOnCard(ecs, input.CardEntityId, upgrade =>
        {
            foreach (ulong id in _spawnGroupScratch)
                upgrade.OnSpawnAtPointCardPlayed?.Invoke(id, ecs);
        });

        // A just-applied upgrade (e.g. HealthBonusUpgrade) can raise an entity's effective
        // max health via a StatModifierComponent — but TroopCardHelper/BuildingSpawnHelper
        // already set CurrentHealth to the pre-upgrade base MaxHealth before any of the loop
        // above ran (upgrades are applied to the entity AFTER it's spawned, not before), so
        // without this each entity would spawn missing exactly the upgrade's bonus instead of
        // at full health. Re-synced here, once per spawned entity, after every upgrade has
        // had a chance to modify max health.
        ComponentStore<HealthComponent> healthStore = ecs.GetComponentStore<HealthComponent>();
        if (healthStore != null)
        {
            foreach (ulong id in _spawnGroupScratch)
            {
                if (!healthStore.HasComponent(id)) continue;

                ref HealthComponent health = ref healthStore.GetComponent(id);
                health.CurrentHealth = StatsQuery.GetMaxHealth(ecs, id, health.CurrentHealth);
                ecs.Delta.MarkComponentDirty(id, typeof(HealthComponent));
            }
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

        CardReturnHelper.OnCardPlayed(ecs, cardStore, input.CardEntityId, card.OwnerPlayerId, definition.Category);
    }
}
