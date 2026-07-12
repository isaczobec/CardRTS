// Query-style request: use RequestManager.Process to run all subscribers synchronously
// and read back IsSelectable. Subscribers set IsSelectable = false to veto; they should
// not call Cancel() so every subscriber gets a chance to weigh in.
public class IsSelectableRequest : Request
{
    public readonly ulong EntityId;
    public bool IsSelectable = true;

    public IsSelectableRequest(ulong entityId)
    {
        EntityId = entityId;
    }

    public override void Execute(ECS ecs) { }
}
