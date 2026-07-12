using System;
using System.Collections.Generic;

// Lets systems queue up requests for something to happen (CreateRequest) and lets other
// code observe or veto them before they run (Subscribe). Usage:
//
//   ecs.Requests.CreateRequest(new DamageRequest(...));
//   ...
//   ecs.Requests.Subscribe<DamageRequest>((req, ecs) => { if (isInvulnerable) req.Cancel(); });
//   ...
//   ecs.Requests.Flush<DamageRequest>(ecs);
//
// Subscribed callbacks for a given request type fire in registration order. If a
// callback cancels the request, no further callbacks run for it and its own Execute
// is skipped.
public class RequestManager
{
    private readonly Dictionary<Type, object> _queues = new();

    private class RequestQueue<T> where T : Request
    {
        public readonly List<Action<T, ECS>> Callbacks = new();
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

    public void CreateRequest<T>(T request) where T : Request
        => GetOrCreateQueue<T>().Pending.Enqueue(request);

    // Dequeues every currently pending request of type T, in the order they were
    // created. For each one, runs subscribed callbacks in registration order (stopping
    // early if a callback cancels it via Request.Cancel()), then calls the request's
    // Execute unless it ended up cancelled.
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
                request.Execute(ecs);
        }
    }

    // Runs all subscribed callbacks on a single request synchronously without
    // enqueueing it. Returns the request so the caller can inspect its final state
    // (e.g. read back a result field that callbacks may have set). If
    // executeIfNotCancelled is true and no callback cancelled the request,
    // Execute is also called.
    public T Process<T>(T request, ECS ecs, bool executeIfNotCancelled = true) where T : Request
    {
        RequestQueue<T> queue = GetOrCreateQueue<T>();

        foreach (Action<T, ECS> callback in queue.Callbacks)
        {
            if (request.IsCancelled) break;
            callback(request, ecs);
        }

        if (executeIfNotCancelled && !request.IsCancelled)
            request.Execute(ecs);

        return request;
    }
}
