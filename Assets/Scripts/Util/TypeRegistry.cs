using System;
using System.Collections.Generic;

public class TypeRegistry<BaseType>
{
    private readonly Dictionary<ushort, Type> idToType = new();
    private  readonly Dictionary<Type, ushort> typeToID = new();

    public void Register<T>(ushort id) where T : BaseType
    {
        idToType[id] = typeof(T);
        typeToID[typeof(T)] = id;
    }

    public Type GetTypeForID(ushort id)
    {
        bool found = idToType.TryGetValue(id, out var type);
        if (!found)
        {
            throw new KeyNotFoundException($"No type registered for ID {id}");
        }
        return type;
    }

    public ushort GetIDForType(Type type)
    {
        bool found = typeToID.TryGetValue(type, out var id);
        if (!found)
        {
            throw new KeyNotFoundException($"No ID registered for type {type}");
        }
        return id;
    }

}