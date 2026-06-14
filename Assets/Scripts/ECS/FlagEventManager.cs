using System;
using System.Collections.Generic;

public class FlagEventManager
{
    private readonly EventDispatcher<FlagEvent> _dispatcher = new();

    // Maps the caller's Action to the Action<T> wrapper passed to the dispatcher,
    // so Unsubscribe can look it up without the caller needing to know about it.
    private readonly Dictionary<Action, Delegate> _wrapperMap = new();

    public void Subscribe<T>(Action callback) where T : FlagEvent
    {
        Action<T> wrapper = _ => callback();
        _wrapperMap[callback] = wrapper;
        _dispatcher.Subscribe<T>(wrapper);
    }

    public void Unsubscribe<T>(Action callback) where T : FlagEvent
    {
        if (!_wrapperMap.TryGetValue(callback, out var wrapper)) return;
        _dispatcher.Unsubscribe<T>((Action<T>)wrapper);
        _wrapperMap.Remove(callback);
    }

    public void Add<T>() where T : FlagEvent, new() => _dispatcher.Raise(new T());

    public void Flush() => _dispatcher.Flush();
}
