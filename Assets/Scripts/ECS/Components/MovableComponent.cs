public enum MovementMode : byte
{
    NotMoving = 0,
    MoveToDestination = 1,
    MoveToPlayerSetDestination = 2,
}

public struct MovableComponent : IComponent
{
    public float destinationX;
    public float destinationY;

    public float playerSetDestinationX;
    public float playerSetDestinationY;

    public bool playerDestinationSet;
    public MovementMode currentMovementMode;

    // "Leash" point an AI-driven troop returns to once it has no targets left (see
    // BasicMeleeAISystem/BasicRangedAISystem's GoHome). Lives here — shared by every
    // movable troop — rather than duplicated on each AI component, so code that just
    // needs to read/re-home it (PathfindingSystem on an explicit move order,
    // BuildingBlockingSystem when relocating a troop) doesn't need a separate path per
    // AI type.
    public float LeashX;
    public float LeashY;

    // ECS.CurrentSimulationTick at the moment TeleportingModifierSystem last moved this
    // entity's PositionComponent — compared against the active ECS's CurrentSimulationTick by
    // TickPositionInterpolator callers to snap the rendered position instantly instead of
    // lerping across the (potentially huge) instantaneous jump, for exactly the one tick the
    // teleport happened on. A plain "true for one tick" bool would need a system to reset it
    // the following tick, which would have to live as a system-instance field — and a
    // system-instance field is NOT restored by RunReconciliation's
    // ClientLocalECS.CopyStateFrom(ClientServerMirrorECS) (that only replaces component-store
    // data), so it would go stale/desynced the moment a reconciliation rewinds the ECS
    // underneath it. Stamping the tick number here instead needs no reset step at all — it's
    // genuine component data, so it's wiped and correctly re-derived by every reconciliation
    // replay exactly like every other field on this struct.
    public ulong TeleportedTick;

    public float CurrentDestinationX => currentMovementMode == MovementMode.MoveToPlayerSetDestination ? playerSetDestinationX : destinationX;
    public float CurrentDestinationY => currentMovementMode == MovementMode.MoveToPlayerSetDestination ? playerSetDestinationY : destinationY;

}
