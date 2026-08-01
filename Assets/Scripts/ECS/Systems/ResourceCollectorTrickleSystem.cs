// Every PeriodTicks simulation ticks, checks whether each player currently has at least one
// "online" (activated) troop that CanCollectResources (see ResourceValueComponent's own doc
// comment — a real, permanent physical troop, not a building or a short-lived summon). If
// they don't, they have no way to go kill a map resource node and earn Wood/Stone/Metal on
// their own, so this looks for a card that WOULD give them one (Card.CanCollectResources):
// first among the cards currently in their hand, falling back to their deck if hand has none
// at all. If none of the qualifying hand cards are currently affordable (or, in the deck
// fallback case, the cheapest qualifying deck card isn't), this trickles Wood/Stone/Metal
// toward affording that one card — TrickleRatePerSecond each, and ONLY for whichever of the
// three the player is still short on (a resource they already have enough of for that card
// is left alone, even while another one keeps ticking up — explicit design ask). Gems/
// Soulstones/Gold are never touched by this at all.
//
// Purely a read-and-grant step recomputed fresh every PeriodTicks ticks from live component
// data — like ResourceValueTotalSystem, it needs no state of its own across ticks, so
// replaying it during reconciliation is harmless. Unlike that system, this is NOT gated
// server-only: it grants resources via ResourcesAdded, which mutates PlayerResourcesComponent
// (client-predicted, gameplay-critical state that afford-checks read directly — see
// ResourceGeneratorSystem/ResourceGenerationSystem's own identical reasoning), so it must run
// deterministically on both the server and every predicting client to stay in sync.
public static class ResourceCollectorTrickleSystem
{
    public const int PeriodTicks = 20; // 1 second at TickManager.TickInterval (0.05s)
    private const float PeriodSeconds = 1f;

    // "at the rate of 4/second" — explicit design ask.
    private const float TrickleRatePerSecond = 4f;
    private const float AmountPerPeriod = TrickleRatePerSecond * PeriodSeconds;

    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    // Reused across every ScanQualifyingCards call this tick rather than allocated fresh —
    // a plain struct return value, so no reference escapes/aliases between calls.
    private struct QualifyingCardScan
    {
        public bool HasAny;
        public bool CanAffordAny;
        public Card Cheapest;
        public float CheapestUnifiedCost;
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        if (ecs.CurrentSimulationTick % PeriodTicks != 0) return;

        ComponentStore<PlayerComponent> playerStore = ecs.GetComponentStore<PlayerComponent>();
        ComponentStore<ResourceValueComponent> valueStore = ecs.GetComponentStore<ResourceValueComponent>();
        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (playerStore == null || valueStore == null || cardStore == null || resourceStore == null) return;

        playerStore.ForEach((ulong id) =>
            ProcessPlayer(ecs, playerStore.GetComponent(id).PlayerId, valueStore, cardStore, resourceStore));
    }

    private static void ProcessPlayer(ECS ecs, ushort playerId,
        ComponentStore<ResourceValueComponent> valueStore,
        ComponentStore<CardComponent> cardStore,
        ComponentStore<PlayerResourcesComponent> resourceStore)
    {
        if (PlayerHasOnlineCollector(ecs, playerId, valueStore)) return;

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, playerId);
        if (resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId)) return;

        PlayerResourcesComponent resources = resourceStore.GetComponent(resourceEntityId);

        QualifyingCardScan handScan = ScanQualifyingCards(playerId, CardLocation.Hand, cardStore, resources);

        Card target;
        if (handScan.HasAny)
        {
            if (handScan.CanAffordAny) return; // can already play one — no boost needed
            target = handScan.Cheapest;
        }
        else
        {
            QualifyingCardScan deckScan = ScanQualifyingCards(playerId, CardLocation.Deck, cardStore, resources);
            if (!deckScan.HasAny) return; // nothing anywhere that could ever collect resources for them
            target = deckScan.Cheapest;
        }

        GrantTowardCard(ecs, resourceEntityId, target.Cost, resources);
    }

    private static bool PlayerHasOnlineCollector(ECS ecs, ushort playerId, ComponentStore<ResourceValueComponent> valueStore)
    {
        bool found = false;
        valueStore.ForEach((ulong id) =>
        {
            if (found) return;
            ResourceValueComponent value = valueStore.GetComponent(id);
            if (value.OwnerPlayerId != playerId) return;
            if (!value.CanCollectResources) return;
            if (!ActivationQuery.IsActivated(ecs, id)) return; // still deploying, dead, or otherwise expired
            found = true;
        });
        return found;
    }

    private static QualifyingCardScan ScanQualifyingCards(ushort playerId, CardLocation location,
        ComponentStore<CardComponent> cardStore, PlayerResourcesComponent resources)
    {
        QualifyingCardScan scan = new QualifyingCardScan { CheapestUnifiedCost = float.MaxValue };

        cardStore.ForEach((ulong id) =>
        {
            CardComponent card = cardStore.GetComponent(id);
            if (card.OwnerPlayerId != playerId || card.Location != location) return;
            if (!CardRegistry.TryGet(card.Type, out Card definition)) return;
            if (!definition.CanCollectResources) return;

            scan.HasAny = true;
            if (definition.Cost.CanAfford(resources)) scan.CanAffordAny = true;

            float unifiedCost = UnifiedCost(definition.Cost);
            if (unifiedCost < scan.CheapestUnifiedCost)
            {
                scan.CheapestUnifiedCost = unifiedCost;
                scan.Cheapest = definition;
            }
        });

        return scan;
    }

    // Same "unified value" weights ResourceValueTotalSystem uses for net worth — reused here
    // purely as a single comparable scalar to rank cards by cost, not as a value judgment.
    private static float UnifiedCost(ResourceCost cost) =>
        cost.Wood       * ResourceConversionRates.Wood +
        cost.Stone      * ResourceConversionRates.Stone +
        cost.Metal      * ResourceConversionRates.Metal +
        cost.Gems       * ResourceConversionRates.Gems +
        cost.Soulstones * ResourceConversionRates.Soulstones +
        cost.Gold       * ResourceConversionRates.Gold;

    private static void GrantTowardCard(ECS ecs, ulong resourceEntityId, ResourceCost cost, PlayerResourcesComponent resources)
    {
        if (cost.CanAfford(resources)) return; // reachable via the deck fallback, which skips the hand branch's own affordability check

        if (resources.Wood < cost.Wood) Grant(ecs, resourceEntityId, ResourceType.Wood);
        if (resources.Stone < cost.Stone) Grant(ecs, resourceEntityId, ResourceType.Stone);
        if (resources.Metal < cost.Metal) Grant(ecs, resourceEntityId, ResourceType.Metal);
    }

    // Enqueued (not flushed here) — ResourceGenerationSystem, registered later in TickManager,
    // flushes every pending ResourcesAdded request this same tick. X/Y left at
    // NO_WORLD_LOCATION — this trickle isn't tied to any spot in the world.
    private static void Grant(ECS ecs, ulong resourceEntityId, ResourceType type)
        => ecs.Requests.CreateRequest(new ResourcesAdded(resourceEntityId, type, AmountPerPeriod));
}
