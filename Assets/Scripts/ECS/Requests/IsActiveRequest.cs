// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back IsActive. Subscribers set IsActive = false to veto; they should not call
// Cancel() so every subscriber gets a chance to weigh in. Mirrors IsSelectableRequest.
public class IsActiveRequest : Request
{
    public readonly ulong EntityId;
    public bool IsActive = true;

    public IsActiveRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs) { }
}
