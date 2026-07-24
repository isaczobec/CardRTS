// Query-style request: use RequestManager.Process to run all subscribers synchronously and
// read back CanMoveOnOwnAccount. Subscribers set CanMoveOnOwnAccount = false to veto; they
// should not call Cancel() so every subscriber gets a chance to weigh in.
//
// Narrower than CanMoveRequest: CanMoveRequest asks "can this entity's position change at
// all, by any means (except teleportation, which never checks either request)" — true for a
// displaced troop, since it's still moving, just not by its own doing. This request instead
// asks "can this entity move itself, from its own movement input/pathing" — checked by
// PathfindingSystem before it moves a troop toward its destination — so e.g. DisplacementSystem
// vetoes this (not CanMoveRequest) while a knockback is in progress: the troop genuinely is
// moving, so CanMoveRequest should stay true, but the player/its own AI shouldn't be able to
// steer it while that's happening.
public class CanMoveOnOwnAccountRequest : Request
{
    public readonly ulong EntityId;
    public bool CanMoveOnOwnAccount = true;

    public CanMoveOnOwnAccountRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs) { }
}
