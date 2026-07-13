using System;
using System.Collections.Generic;

// Lets systems queue up requests for something to happen (CreateRequest) and lets other
// code observe or veto them before they run (Subscribe), or react once they've fully run
// (SubscribeExecuted). Usage:
//
//   ecs.Requests.CreateRequest(new DamageRequest(...));
//   ...
//   ecs.Requests.Subscribe<DamageRequest>((req, ecs) => { if (isInvulnerable) req.Cancel(); });
//   ecs.Requests.SubscribeExecuted<DamageRequest>((req, ecs) => { ... });
//   ...
//   ecs.Requests.Flush<DamageRequest>(ecs);
//
// Subscribed callbacks for a given request type fire in registration order. If a
// callback cancels the request, no further Subscribe callbacks run for it and its own
// Execute is skipped (SubscribeExecuted callbacks never run for a cancelled request
// either, since Execute — the thing they're notified "executed" happened — never ran).
//
// SubscribeExecuted callbacks normally fire automatically right after a request's Execute
// returns (see Flush/Process). A request's own Execute can instead call
// ecs.NotifyRequestExecuted(this) (or Requests.NotifyExecuted) itself, before doing any
// cleanup that would remove state those callbacks need to read (e.g. deleting the entity
// Execute just acted on) — NotifyExecuted only ever fires a given request's callbacks
// once, so the automatic post-Execute call becomes a no-op if Execute already triggered it.
public class RequestManager
{
    private readonly Dictionary<Type, object> _queues = new();

    private class RequestQueue<T> where T : Request
    {
        public readonly List<Action<T, ECS>> Callbacks = new();
        public readonly List<Action<T, ECS>> ExecutedCallbacks = new();
        public readonly Queue<T> Pending = new();
    }

    private RequestQueue<T> GetOrCreateQueue<T>() where T : Request
    {
        Type type = typeof(T);
        if (!_queues.TryGetValue(type, out object queue))
        {
            queue = new RequestQueue<T>();
            _queues[type] = queue;
        }
        return (RequestQueue<T>)queue;
    }

    public void Subscribe<T>(Action<T, ECS> callback) where T : Request
        => GetOrCreateQueue<T>().Callbacks.Add(callback);

    public void Unsubscribe<T>(Action<T, ECS> callback) where T : Request
        => GetOrCreateQueue<T>().Callbacks.Remove(callback);

    // Registers a callback to run once a request of type T has fully executed — see the
    // class doc comment for exactly when that fires.
    public void SubscribeExecuted<T>(Action<T, ECS> callback) where T : Request
        => GetOrCreateQueue<T>().ExecutedCallbacks.Add(callback);

    public void UnsubscribeExecuted<T>(Action<T, ECS> callback) where T : Request
        => GetOrCreateQueue<T>().ExecutedCallbacks.Remove(callback);

    public void CreateRequest<T>(T request) where T : Request
        => GetOrCreateQueue<T>().Pending.Enqueue(request);

    // Runs request's SubscribeExecuted callbacks, in registration order, if they haven't
    // already run for it. Safe to call multiple times (or not at all — Flush/Process call
    // this automatically after Execute) since it no-ops after the first call for a given
    // request instance.
    public void NotifyExecuted<T>(T request, ECS ecs) where T : Request
    {
        if (request.WasExecutedNotified) return;
        request.MarkExecutedNotified();

        RequestQueue<T> queue = GetOrCreateQueue<T>();
        foreach (Action<T, ECS> callback in queue.ExecutedCallbacks)
            callback(request, ecs);
    }

    // Dequeues every currently pending request of type T, in the order they were
    // created. For each one, runs subscribed callbacks in registration order (stopping
    // early if a callback cancels it via Request.Cancel()), then calls the request's
    // Execute unless it ended up cancelled, then notifies its "executed" subscribers.
    public void Flush<T>(ECS ecs) where T : Request
    {
        RequestQueue<T> queue = GetOrCreateQueue<T>();
        while (queue.Pending.Count > 0)
        {
            T request = queue.Pending.Dequeue();

            foreach (Action<T, ECS> callback in queue.Callbacks)
            {
                if (request.IsCancelled) break;
                callback(request, ecs);
            }

            if (!request.IsCancelled)
            {
                request.Execute(ecs);
                NotifyExecuted(request, ecs);
            }
        }
    }

    // Runs all subscribed callbacks on a single request synchronously without
    // enqueueing it. Returns the request so the caller can inspect its final state
    // (e.g. read back a result field that callbacks may have set). If
    // executeIfNotCancelled is true and no callback cancelled the request,
    // Execute is also called, followed by its "executed" subscribers.
    public T Process<T>(T request, ECS ecs, bool executeIfNotCancelled = true) where T : Request
    {
        RequestQueue<T> queue = GetOrCreateQueue<T>();

        foreach (Action<T, ECS> callback in queue.Callbacks)
        {
            if (request.IsCancelled) break;
            callback(request, ecs);
        }

        if (executeIfNotCancelled && !request.IsCancelled)
        {
            request.Execute(ecs);
            NotifyExecuted(request, ecs);
        }

        return request;
    }
}
