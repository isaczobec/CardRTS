// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back IsActivated. Subscribers set IsActivated = false to veto; they should not
// call Cancel() so every subscriber gets a chance to weigh in. Mirrors CanPerformRequest/
// CanMoveRequest/IsSelectableRequest/IsActiveRequest — deliberately its own request rather
// than always implied by IsActiveRequest, so orthogonal veto reasons (not yet activated,
// dead, expired, ...) can each subscribe independently without needing to know about each
// other. Named for "is this troop activated (and otherwise able to exist/act at all)" —
// distinct from IsActiveRequest, which covers only the ActivatableComponent deploy delay.
public class IsActivatedRequest : Request
{
    public readonly ulong EntityId;
    public bool IsActivated = true;

    public IsActivatedRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs) { }
}
