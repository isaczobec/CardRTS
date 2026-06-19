using System;

public class GlobalSystem : ISystem
{
    private readonly GlobalSystemFunction _function;

    public Type[] ComponentTypes => Array.Empty<Type>();

    public GlobalSystem(GlobalSystemFunction function)
    {
        _function = function;
    }

    public void Execute(ECS ecs) => _function(ecs, ecs.FlagEvents);
}
