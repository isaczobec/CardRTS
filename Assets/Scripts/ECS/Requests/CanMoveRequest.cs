// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back CanMove. Subscribers set CanMove = false to veto; they should not call
// Cancel() so every subscriber gets a chance to weigh in.
//
// Means "can this entity's position change at all, right now, by any means except
// teleportation" (teleportation — see TeleportRequest — never checks this or
// CanMoveOnOwnAccountRequest; it always succeeds). This is broader than
// CanMoveOnOwnAccountRequest, which only covers movement driven by the entity's own
// input/pathing — a displaced (knocked back) troop is still moving, just not steering
// itself, so DisplacementSystem vetoes CanMoveOnOwnAccountRequest but deliberately leaves
// this one alone. A future effect that should freeze an entity in place outright (unlike a
// knockback) belongs here instead.
public class CanMoveRequest : Request
{
    public readonly ulong EntityId;
    public bool CanMove = true;

    public CanMoveRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs) { }
}
