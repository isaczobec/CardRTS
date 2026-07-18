// Reusable modifier payload (alongside ModifierComponent — see ModifierComponent's own doc
// comment for why a modifier is its own entity) that teleports ModifierComponent.TargetEntityId
// to (DestinationX, DestinationY) — see TeleportingModifierSystem. Cards granting this should
// set ModifierComponent.TicksRemaining = int.MaxValue (never expires via ModifierSystem's own
// countdown); TeleportingModifierSystem is solely responsible for deleting the modifier
// entity once the teleport has happened.
public struct TeleportingModifierComponent : IComponent
{
    public float DestinationX;
    public float DestinationY;

    // False until TeleportingModifierSystem has moved the target — guards against
    // re-teleporting every tick while this (already-consumed) entity sits around client-side
    // waiting for the server's delta to actually remove it.
    public bool HasTeleported;
}
