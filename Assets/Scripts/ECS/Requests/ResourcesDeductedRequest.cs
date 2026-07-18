// Deducts cost from resourceEntityId's PlayerResourcesComponent when executed (via
// ResourceHelper.Spend, which also fires ResourcesChangedEvent) — the caller is expected to
// have already checked ResourceCost.CanAfford. Cancel() (inherited from Request) fully
// negates the deduction, e.g. a Subscribe callback could veto it if some other system
// determines the spend shouldn't go through after all.
public class ResourcesDeductedRequest : Request
{
    public readonly ulong ResourceEntityId;
    public readonly ResourceCost Cost;

    public ResourcesDeductedRequest(ulong resourceEntityId, ResourceCost cost)
    {
        ResourceEntityId = resourceEntityId;
        Cost = cost;
    }

    public override void Execute(ECS ecs) => ResourceHelper.Spend(ecs, ResourceEntityId, Cost);
}
