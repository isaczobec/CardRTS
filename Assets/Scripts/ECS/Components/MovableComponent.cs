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

    public float CurrentDestinationX => currentMovementMode == MovementMode.MoveToPlayerSetDestination ? playerSetDestinationX : destinationX;
    public float CurrentDestinationY => currentMovementMode == MovementMode.MoveToPlayerSetDestination ? playerSetDestinationY : destinationY;

}
