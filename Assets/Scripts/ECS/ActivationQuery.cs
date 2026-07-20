// Thin call-site wrapper around IsActiveRequest/IsActivatedRequest/CanPerformRequest/
// CanMoveRequest — Process runs every subscriber synchronously (each vetoes independently
// by setting the result field to false; see the request classes) and returns the request so
// we can read that result back, without it ever being queued/actually executed (every one
// of these requests' Execute is a no-op). Actual veto logic lives wherever owns the
// relevant state and subscribes to these — e.g. ActivationSystem for ActivatableComponent,
// DeathSystem for TroopComponent.IsDead, LifetimeSystem for an expired LifetimeComponent
// countdown — rather than here, so a new veto reason never requires touching this file.
public static class ActivationQuery
{
    public static bool IsActive(ECS ecs, ulong entityId)
        => ecs.Requests.Process(new IsActiveRequest(entityId), ecs, executeIfNotCancelled: false).IsActive;

    // Single guard for "may this entity currently be interacted with / act at all" — i.e.
    // is it activated (past its deploy delay), alive, and not otherwise expired. Use this
    // instead of checking IsActive alone.
    public static bool IsActivated(ECS ecs, ulong entityId)
        => ecs.Requests.Process(new IsActivatedRequest(entityId), ecs, executeIfNotCancelled: false).IsActivated;

    // Narrower guard checked before a troop initiates/finishes an attack or ability (see
    // CanPerformRequest) — e.g. a silence effect vetoes this without affecting movement or
    // activation.
    public static bool CanPerform(ECS ecs, ulong entityId)
        => ecs.Requests.Process(new CanPerformRequest(entityId), ecs, executeIfNotCancelled: false).CanPerform;

    // Narrower guard checked before an entity moves (see CanMoveRequest) — e.g. a root
    // effect vetoes this without affecting attacks/abilities or activation.
    public static bool CanMove(ECS ecs, ulong entityId)
        => ecs.Requests.Process(new CanMoveRequest(entityId), ecs, executeIfNotCancelled: false).CanMove;
}
