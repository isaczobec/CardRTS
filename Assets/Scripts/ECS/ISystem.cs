using System;

public delegate void SingleComponentSystemFunction<T>(ref T component, FlagEventManager flagEvents) where T : struct, IComponent;
public delegate void MultipleComponentSystemFunction(ulong entityId, uint[] indices, ECS ecs, FlagEventManager flagEvents);
public delegate void GlobalSystemFunction(ECS ecs, FlagEventManager flagEvents);

public interface ISystem
{
    Type[] ComponentTypes { get; }
    void Execute(ECS ecs);
    void Setup(ECS ecs);
}
