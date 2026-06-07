public class EntityHandle
{
    public ulong Id { get; }
    public EntityHandle(ulong id) { Id = id; }
}

public struct EntityData
{
    public ulong Id;
    public EntityData(ulong id) { Id = id; }
}
