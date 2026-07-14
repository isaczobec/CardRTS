// Thin call-site wrapper around IsActiveRequest/CanTakeActionsRequest — Process runs every
// subscriber synchronously (each vetoes independently by setting the result field to
// false; see the request classes) and returns the request so we can read that result back,
// without it ever being queued/actually executed (both requests' Execute is a no-op).
// Actual veto logic lives wherever owns the relevant state and subscribes to these — e.g.
// ActivationSystem for ActivatableComponent, DeathSystem for TroopComponent.IsDead,
// LifetimeSystem for an expired LifetimeComponent countdown — rather than here, so a new
// veto reason never requires touching this file.
public static class ActivationQuery
{
    public static bool IsActive(ECS ecs, ulong entityId)
        => ecs.Requests.Process(new IsActiveRequest(entityId), ecs, executeIfNotCancelled: false).IsActive;

    // Single guard for "may this entity currently be interacted with / act". Use this
    // instead of checking IsActive alone.
    public static bool CanTakeActions(ECS ecs, ulong entityId)
        => ecs.Requests.Process(new CanTakeActionsRequest(entityId), ecs, executeIfNotCancelled: false).CanTakeActions;
}
