using UnityEngine;

// Called by SpawnAtPointCardPlaySystem right after a played card's own spawn has been tagged
// with its ResourceValueComponent — if the spawned entity is a building (has
// BuildingComponent) and lands within RangeMultiplier x the Range stat of a friendly,
// active BuildingRefundAuraComponent-carrying troop (see ConstructionWorkerCard), RefundRatio
// (rounded down, per resource) of that building's own ResourceValueComponent is refunded
// straight back to its owner via a ResourcesAdded request per nonzero resource. Only the
// first qualifying troop found refunds anything — explicit "should not stack" design ask, so
// several Construction Workers near the same build site don't multiply the refund.
public static class BuildingRefundHelper
{
    public static void TryRefund(ECS ecs, ulong buildingEntityId, ushort ownerPlayerId)
    {
        ComponentStore<BuildingComponent> buildingStore = ecs.GetComponentStore<BuildingComponent>();
        ComponentStore<PositionComponent> posStore = ecs.GetComponentStore<PositionComponent>();
        ComponentStore<ResourceValueComponent> valueStore = ecs.GetComponentStore<ResourceValueComponent>();
        ComponentStore<BuildingRefundAuraComponent> auraStore = ecs.GetComponentStore<BuildingRefundAuraComponent>();
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (buildingStore == null || posStore == null || valueStore == null || auraStore == null || troopStore == null) return;
        if (!buildingStore.HasComponent(buildingEntityId)) return;
        if (!posStore.HasComponent(buildingEntityId) || !valueStore.HasComponent(buildingEntityId)) return;

        PositionComponent buildingPos = posStore.GetComponent(buildingEntityId);

        ulong workerId = FindQualifyingWorker(ecs, buildingPos, ownerPlayerId, auraStore, troopStore, posStore);
        if (workerId == 0) return;

        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, ownerPlayerId);
        if (resourceEntityId == 0) return;

        float refundRatio = auraStore.GetComponent(workerId).RefundRatio;
        ResourceValueComponent value = valueStore.GetComponent(buildingEntityId);

        Refund(ecs, resourceEntityId, ResourceType.Wood, value.Wood, refundRatio, buildingPos);
        Refund(ecs, resourceEntityId, ResourceType.Stone, value.Stone, refundRatio, buildingPos);
        Refund(ecs, resourceEntityId, ResourceType.Metal, value.Metal, refundRatio, buildingPos);
        Refund(ecs, resourceEntityId, ResourceType.Gems, value.Gems, refundRatio, buildingPos);
        Refund(ecs, resourceEntityId, ResourceType.Soulstones, value.Soulstones, refundRatio, buildingPos);
        Refund(ecs, resourceEntityId, ResourceType.Gold, value.Gold, refundRatio, buildingPos);
    }

    private static void Refund(ECS ecs, ulong resourceEntityId, ResourceType type, int amount, float ratio, PositionComponent pos)
    {
        int refund = Mathf.FloorToInt(amount * ratio);
        if (refund <= 0) return;

        ecs.Requests.Process(new ResourcesAdded(resourceEntityId, type, refund) { X = pos.X, Y = pos.Y }, ecs);
    }

    private static ulong FindQualifyingWorker(ECS ecs, PositionComponent buildingPos, ushort ownerPlayerId,
        ComponentStore<BuildingRefundAuraComponent> auraStore, ComponentStore<TroopComponent> troopStore, ComponentStore<PositionComponent> posStore)
    {
        ulong found = 0;
        auraStore.ForEach((ulong workerId) =>
        {
            if (found != 0) return;
            if (!troopStore.HasComponent(workerId)) return;

            TroopComponent troop = troopStore.GetComponent(workerId);
            if (troop.OwnerPlayerId != ownerPlayerId) return;
            if (troop.IsDead) return;
            if (!posStore.HasComponent(workerId)) return;
            if (!ActivationQuery.IsActivated(ecs, workerId)) return;

            PositionComponent workerPos = posStore.GetComponent(workerId);
            float range = StatsQuery.GetRange(ecs, workerId, 5) * auraStore.GetComponent(workerId).RangeMultiplier;
            float dx = workerPos.X - buildingPos.X;
            float dy = workerPos.Y - buildingPos.Y;
            if (dx * dx + dy * dy > range * range) return;

            found = workerId;
        });
        return found;
    }
}
