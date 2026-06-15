using System;
using System.Collections.Generic;

public class EventDispatcher<TBase>
{
    private readonly Dictionary<Type, List<Action<TBase>>> _subscribers = new();
    private readonly Dictionary<Delegate, Action<TBase>> _wrapperMap = new();
    private readonly List<TBase> _pending = new();

    public void Subscribe<T>(Action<T> callback) where T : TBase
    {
        var type = typeof(T);
        if (!_subscribers.ContainsKey(type))
            _subscribers[type] = new();

        Action<TBase> wrapper = evt => callback((T)evt);
        _wrapperMap[callback] = wrapper;
        _subscribers[type].Add(wrapper);
    }

    public void Unsubscribe<T>(Action<T> callback) where T : TBase
    {
        if (!_wrapperMap.TryGetValue(callback, out var wrapper)) return;
        if (_subscribers.TryGetValue(typeof(T), out var list))
            list.Remove(wrapper);
        _wrapperMap.Remove(callback);
    }

    public IReadOnlyList<TBase> Pending => _pending;

    public void Raise(TBase evt) => _pending.Add(evt);

    public void Flush()
    {
        foreach (var evt in _pending)
            if (_subscribers.TryGetValue(evt.GetType(), out var callbacks))
                foreach (var cb in callbacks)
                    cb(evt);
        _pending.Clear();
    }
}
