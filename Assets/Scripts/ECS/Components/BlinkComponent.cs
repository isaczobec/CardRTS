// Marks an entity (spawned by BlinkCard) that, once activated, teleports every friendly
// physical troop (not buildings) within Range of its own position to (DestinationX,
// DestinationY) — see BlinkSystem. The spawner entity deletes itself (server-only) right
// after granting the teleport modifiers.
public struct BlinkComponent : IComponent
{
    public float DestinationX;
    public float DestinationY;
    public float Range;
}
