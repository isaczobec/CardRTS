using System;

public class SingleComponentSystem<T> : ISystem where T : struct, IComponent
{
    private SingleComponentSystemFunction<T> _function;

    public Type[] ComponentTypes => new[] { typeof(T) };

    public SingleComponentSystem(SingleComponentSystemFunction<T> function)
    {
        _function = function;
    }

    public void Setup(ECS ecs) { }
    public void Execute(ECS ecs) => ecs.GetComponentStore<T>().ForEach(_function);
}
