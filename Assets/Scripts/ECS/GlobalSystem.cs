using System;

public class GlobalSystem : ISystem
{
    private readonly GlobalSystemFunction _function;
    private readonly Action<ECS> _setup;

    public Type[] ComponentTypes => Array.Empty<Type>();

    public GlobalSystem(GlobalSystemFunction function, Action<ECS> setup = null)
    {
        _function = function;
        _setup = setup;
    }

    public void Setup(ECS ecs) => _setup?.Invoke(ecs);
    public void Execute(ECS ecs) => _function(ecs, ecs.FlagEvents);
}
