using System;
using System.Collections.Generic;

public class FlagEventManager
{
    private Dictionary<Type, List<Action>> _subscribers = new();
    private List<Type> _pending = new();

    public void Subscribe<T>(Action callback)
    {
        var type = typeof(T);
        if (!_subscribers.ContainsKey(type))
            _subscribers[type] = new List<Action>();
        _subscribers[type].Add(callback);
    }

    public void Unsubscribe<T>(Action callback)
    {
        if (_subscribers.TryGetValue(typeof(T), out var callbacks))
            callbacks.Remove(callback);
    }

    public void Add<T>()
    {
        _pending.Add(typeof(T));
    }

    public void Flush()
    {
        foreach (var type in _pending)
        {
            if (_subscribers.TryGetValue(type, out var callbacks))
                foreach (var callback in callbacks)
                    callback();
        }
        _pending.Clear();
    }
}
