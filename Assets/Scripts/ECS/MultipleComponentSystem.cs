using System;

public class MultipleComponentSystem : ISystem
{
    private MultipleComponentSystemFunction _function;
    private Type[] _componentTypes;

    public Type[] ComponentTypes => _componentTypes;

    public MultipleComponentSystem(Type[] componentTypes, MultipleComponentSystemFunction function)
    {
        _componentTypes = componentTypes;
        _function = function;
    }

    public void Setup(ECS ecs) { }

    public void Execute(ECS ecs)
    {
        IComponentStore[] stores = new IComponentStore[_componentTypes.Length];
        IComponentStore shortest = null;

        for (int i = 0; i < _componentTypes.Length; i++)
        {
            IComponentStore s = ecs.GetIComponentStore(_componentTypes[i]);
            stores[i] = s;
            if (shortest == null || s.Size < shortest.Size)
                shortest = s;
        }

        uint[] indices = new uint[_componentTypes.Length];

        shortest.ForEach(id =>
        {
            for (int i = 0; i < stores.Length; i++)
            {
                if (!stores[i].HasComponent(id)) return;
                indices[i] = stores[i].IdToIndex(id);
            }
            _function(id, indices, ecs, ecs.FlagEvents);
        });
    }
}
