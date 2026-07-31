using System.Collections.Generic;

// Reads BuyUpgradeInput each tick: validates the requested UpgradeType is real, the target
// card belongs to the requesting client, and the buying player can afford the upgrade's
// ShopGoldCost, then deducts that cost via a ResourcesDeductedRequest — mirrors
// BuyCardSystem's own predicted/server-only split (resource deduction is a safe-to-predict
// component mutation; actually equipping the upgrade is server-only, confirmed to the buyer
// via the normal delta stream like any other server-created/mutated state).
public static class BuyUpgradeSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute);

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        List<BuyUpgradeInput> inputs = ecs.GetInputsForTick<BuyUpgradeInput>();
        if (inputs == null || inputs.Count == 0) return;

        foreach (BuyUpgradeInput input in inputs)
            BuyUpgrade(ecs, input);
    }

    private static void BuyUpgrade(ECS ecs, BuyUpgradeInput input)
    {
        if (!UpgradeRegistry.TryGet(input.UpgradeType, out CardUpgrade definition))
        {
            DebugLogger.LogWarning($"[BuyUpgradeSystem] Rejected: upgrade type {input.UpgradeType} not found in UpgradeRegistry (client {input.ClientId}).", "cards");
            return;
        }

        ComponentStore<CardComponent> cardStore = ecs.GetComponentStore<CardComponent>();
        if (cardStore == null || !cardStore.HasComponent(input.TargetCardEntityId))
        {
            DebugLogger.LogWarning($"[BuyUpgradeSystem] Rejected: target card {input.TargetCardEntityId} has no CardComponent (client {input.ClientId}).", "cards");
            return;
        }

        CardComponent targetCard = cardStore.GetComponent(input.TargetCardEntityId);
        if (targetCard.OwnerPlayerId != input.ClientId)
        {
            DebugLogger.LogWarning($"[BuyUpgradeSystem] Rejected: target card {input.TargetCardEntityId} owner {targetCard.OwnerPlayerId} != requesting client {input.ClientId}.", "cards");
            return;
        }

        int currentStackCount = UpgradeQuery.CountUpgradesOfType(ecs, input.TargetCardEntityId, input.UpgradeType);
        if (currentStackCount >= definition.MaxStackCount)
        {
            DebugLogger.LogWarning($"[BuyUpgradeSystem] Rejected: card {input.TargetCardEntityId} already has {currentStackCount}/{definition.MaxStackCount} of upgrade {input.UpgradeType} (client {input.ClientId}).", "cards");
            return;
        }

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, input.ClientId);
        ComponentStore<PlayerResourcesComponent> resourceStore = ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || resourceEntityId == 0 || !resourceStore.HasComponent(resourceEntityId))
        {
            DebugLogger.LogWarning($"[BuyUpgradeSystem] Rejected: no PlayerResourcesComponent found for player {input.ClientId}.", "cards");
            return;
        }

        ResourceCost shopCost = new ResourceCost { Gold = definition.ShopGoldCost };
        PlayerResourcesComponent resources = resourceStore.GetComponent(resourceEntityId);
        if (!shopCost.CanAfford(resources))
        {
            DebugLogger.LogWarning($"[BuyUpgradeSystem] Rejected: player {input.ClientId} can't afford {input.UpgradeType} (shop cost {definition.ShopGoldCost} gold, has {resources.GoldFloor}).", "cards");
            return;
        }

        ecs.Requests.Process(new ResourcesDeductedRequest(resourceEntityId, shopCost), ecs);

        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        if (!isServer) return;

        // A new entity per purchase (not a component slot on the card) — see
        // UpgradeComponent's doc comment — so the same card can carry any number of
        // upgrades, including more than one of the same Type.
        EntityHandle upgrade = ecs.CreateEntity();
        ecs.AddComponent(upgrade.Id, new UpgradeComponent
        {
            TargetCardEntityId = input.TargetCardEntityId,
            Type               = input.UpgradeType,
        });
        ResourceValueHelper.Attach(ecs, upgrade.Id, input.ClientId, shopCost);
    }
}
