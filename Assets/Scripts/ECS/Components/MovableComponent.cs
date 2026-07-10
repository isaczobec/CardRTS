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

    public float CurrentDestinationX => currentMovementMode == MovementMode.MoveToPlayerSetDestination ? playerSetDestinationX : destinationX;
    public float CurrentDestinationY => currentMovementMode == MovementMode.MoveToPlayerSetDestination ? playerSetDestinationY : destinationY;

}
