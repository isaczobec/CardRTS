// Base type for one-shot, cancellable actions queued via RequestManager (e.g. "deal N
// damage to entity X"). Subtypes carry whatever data the request needs and implement
// Execute to actually apply it.
public abstract class Request
{
    public bool IsCancelled { get; private set; }

    // Marks this request as cancelled. Any callbacks still pending for it are skipped,
    // and Execute is never called.
    public void Cancel() => IsCancelled = true;

    // Set once RequestManager.NotifyExecuted has fired this request's "executed"
    // callbacks (see RequestManager.SubscribeExecuted) — guards against firing them twice
    // when a request calls ecs.NotifyRequestExecuted/Requests.NotifyExecuted on itself
    // (e.g. to let subscribers read state before Execute deletes an entity) and
    // RequestManager's Flush/Process would otherwise also fire them right after Execute
    // returns.
    public bool WasExecutedNotified { get; private set; }
    internal void MarkExecutedNotified() => WasExecutedNotified = true;

    public abstract void Execute(ECS ecs);
}
