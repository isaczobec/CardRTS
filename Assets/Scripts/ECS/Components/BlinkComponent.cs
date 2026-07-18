// Marks an entity (spawned by BlinkCard) that, once activated, teleports every friendly
// physical troop (not buildings) within Range of its own position to (DestinationX,
// DestinationY) — see BlinkSystem. The spawner entity deletes itself (server-only) right
// after granting the teleport modifiers.
public struct BlinkComponent : IComponent
{
    public float DestinationX;
    public float DestinationY;
    public float Range;

    // False until BlinkSystem has granted teleport modifiers to nearby troops — guards
    // against re-granting them every tick while this (already-consumed) entity sits around
    // client-side waiting for the server's delta to actually remove it. Stored on the
    // component (not a system-instance field) so it's correctly restored by reconciliation's
    // ClientLocalECS.CopyStateFrom the same way every other component field is — see
    // TeleportingModifierSystem's doc comment for why a system-instance field would go stale
    // across a reconciliation rewind.
    public bool HasFired;
}
