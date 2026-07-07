// Base type for one-shot, cancellable actions queued via RequestManager (e.g. "deal N
// damage to entity X"). Subtypes carry whatever data the request needs and implement
// Execute to actually apply it.
public abstract class Request
{
    public bool IsCancelled { get; private set; }

    // Marks this request as cancelled. Any callbacks still pending for it are skipped,
    // and Execute is never called.
    public void Cancel() => IsCancelled = true;

    public abstract void Execute(ECS ecs);
}
