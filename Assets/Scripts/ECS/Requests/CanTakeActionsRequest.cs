// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back CanTakeActions. Subscribers set CanTakeActions = false to veto; they
// should not call Cancel() so every subscriber gets a chance to weigh in. Mirrors
// IsSelectableRequest/IsActiveRequest — deliberately its own request rather than always
// implied by IsActiveRequest, so orthogonal veto reasons (not yet activated, dead, expired,
// ...) can each subscribe independently without needing to know about each other.
public class CanTakeActionsRequest : Request
{
    public readonly ulong EntityId;
    public bool CanTakeActions = true;

    public CanTakeActionsRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs) { }
}
