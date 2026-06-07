using System;

public delegate void SingleComponentSystemFunction<T>(ref T component, FlagEventManager flagEvents) where T : struct, IComponent;
public delegate void MultipleComponentSystemFunction(uint[] indices, ECS ecs, FlagEventManager flagEvents);

public interface ISystem
{
    Type[] ComponentTypes { get; }
    void Execute(ECS ecs);
}
