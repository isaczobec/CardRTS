using System.Collections.Generic;

public interface IComponentChangedEvent
{
    IReadOnlyList<ulong> EntityIds { get; }
}

public class ComponentChangedEvent<T> : IComponentChangedEvent where T : IComponent
{
    public IReadOnlyList<ulong> EntityIds { get; }
    public ComponentChangedEvent(List<ulong> entityIds) => EntityIds = entityIds;
}
