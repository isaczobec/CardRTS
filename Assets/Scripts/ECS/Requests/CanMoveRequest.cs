// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back CanMove. Subscribers set CanMove = false to veto; they should not call
// Cancel() so every subscriber gets a chance to weigh in. Checked before an entity is
// allowed to move — so e.g. a future root effect can subscribe here without touching
// attacks/abilities (CanPerformRequest) or activation (IsActivatedRequest) at all. Mirrors
// those same requests' shape.
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
