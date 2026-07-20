// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back CanPerform. Subscribers set CanPerform = false to veto; they should not
// call Cancel() so every subscriber gets a chance to weigh in. Checked before a troop
// initiates or finishes an action — an attack windup/impact, an ability cast — so e.g. a
// future silence effect can subscribe here without touching movement (CanMoveRequest) or
// activation (IsActivatedRequest) at all. Mirrors those same requests' shape.
public class CanPerformRequest : Request
{
    public readonly ulong EntityId;
    public bool CanPerform = true;

    public CanPerformRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs) { }
}
