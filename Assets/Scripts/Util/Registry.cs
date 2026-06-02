using System;
using System.Collections.Generic;
using UnityEngine;

public class Registry<T> where T : class
{
    private Dictionary<ulong, T> referenceRegistry = new Dictionary<ulong, T>();
    private Dictionary<T, ulong> idRegistry = new Dictionary<T, ulong>();

    public void Register(T obj, ulong id)
    {
        if (referenceRegistry.ContainsKey(id))
        {
            Debug.LogWarning($"Object with ID {id} was already in the registry, returning!");
            return;
        }
        referenceRegistry.Add(id, obj);
        idRegistry.Add(obj, id);
    }

    public void Deregister(ulong id)
    {
        if (referenceRegistry.TryGetValue(id, out T obj))
        {
            referenceRegistry.Remove(id);
            idRegistry.Remove(obj);
        }
        else
        {
            Debug.LogWarning($"Object with ID {id} did not exist in the registry!");
        }
    }

    public ulong GetId(T obj)
    {
        if (idRegistry.TryGetValue(obj, out ulong id))
        {
            return id;
        }
        Debug.LogWarning($"The object {obj} did not exist!");
        return ulong.MaxValue;
    }

    public T GetObject(ulong id, bool suppressWarning = false)
    {
        if (referenceRegistry.TryGetValue(id, out T obj))
        {
            return obj;
        }
        if (!suppressWarning) Debug.LogWarning($"The object with ID {id} did not exist!");
        return null;
    }

}