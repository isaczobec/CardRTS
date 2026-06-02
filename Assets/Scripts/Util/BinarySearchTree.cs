using System;
using System.Collections;
using System.Collections.Generic;

public class BinarySearchTree<Key, Value>
    where Key : IComparable<Key>
    where Value : class
{
    private const uint RESIZE_FACTOR = 2;
    private Key[] keys;
    private Value[] values;
    private uint count = 0;
    private uint capacity = 0;

    public BinarySearchTree(uint initialCapacity)
    {
        if (initialCapacity == 0)
            throw new ArgumentException("Capacity must be > 0");

        keys = new Key[initialCapacity];
        values = new Value[initialCapacity];
        capacity = initialCapacity;
    }

    private int FindIndexOfKey(Key key)
    {
        uint current = 1; // using 1-based tree logic
        while (current <= capacity && values[current - 1] != null)
        {
            int comparison = key.CompareTo(keys[current - 1]);
            if (comparison < 0)
            {
                current = current * 2; // Go left
            }
            else if (comparison > 0)
            {
                current = current * 2 + 1; // Go right
            }
            else
            {
                return (int)(current - 1); // Found
            }
        }
        return -1; // Not found
    }

    public Value Get(Key key)
    {
        int index = FindIndexOfKey(key);
        if (index == -1)
        {
            return null; // Not found
        }
        return values[index];
    }

    public void Add(Key key, Value value)
    {
        if (count >= capacity)
        {
            ResizeGrow();
        }

        uint current = 1;
        while (true)
        {
            if (current > capacity)
            {
                ResizeGrow();
            }

            // add if empty
            if (values[current - 1] == null)
            {
                keys[current - 1] = key;
                values[current - 1] = value;
                count++;
                return;
            }

            int comparison = key.CompareTo(keys[current - 1]);
            if (comparison < 0)
            {
                current = current * 2; // Go left
            }
            else if (comparison > 0)
            {
                current = current * 2 + 1; // Go right
            }
            else
            {
                // overwrite if same key
                values[current - 1] = value;
                return;
            }
        }
    }

    public void Remove(Key key)
    {
        int idx = FindIndexOfKey(key);
        if (idx == -1)
        {
            return; // not found
        }
        uint index = (uint)(idx + 1);

        // case 1: no children
        bool hasLeft = index * 2 - 1 < capacity && values[index * 2 - 1] != null;
        bool hasRight = index * 2 < capacity && values[index * 2] != null;

        if (!hasLeft && !hasRight)
        {
            keys[index - 1] = default;
            values[index - 1] = null;
            count--;
            return;
        }

        // case 2: two children
        if (hasLeft && hasRight)
        {
            uint successorIndex = index * 2 + 1;
            while (successorIndex * 2 - 1 < capacity && values[successorIndex * 2 - 1] != null)
            {
                successorIndex = successorIndex * 2;
            }

            keys[index - 1] = keys[successorIndex - 1];
            values[index - 1] = values[successorIndex - 1];

            keys[successorIndex - 1] = default;
            values[successorIndex - 1] = null;
            count--;
            return;
        }

        // case 3: only one child
        if (hasLeft)
        {
            MoveUpSubtree(index * 2);
            count--;
        }
        else if (hasRight)
        {
            MoveUpSubtree(index * 2 + 1);
            count--;
        }
    }

    private void MoveUpSubtree(uint index)
    {
        if (index >= capacity || values[index - 1] == null) return;

        uint parentIndex = index / 2;

        keys[parentIndex - 1] = keys[index - 1];
        values[parentIndex - 1] = values[index - 1];

        keys[index - 1] = default;
        values[index - 1] = null;

        if (index * 2 <= capacity && values[index * 2 - 1] != null)
        {
            MoveUpSubtree(index * 2);
        }

        if (index * 2 + 1 <= capacity && values[index * 2] != null)
        {
            MoveUpSubtree(index * 2 + 1);
        }
    }

    private void ResizeGrow()
    {
        capacity *= RESIZE_FACTOR;
        Array.Resize(ref keys, (int)capacity);
        Array.Resize(ref values, (int)capacity);
    }

    public IEnumerable<Value> GetAll()
    {
        for (uint i = 0; i < capacity; i++)
        {
            if (values[i] != null)
            {
                yield return values[i];
            }
        }
    }

    public IEnumerable<KeyValuePair<Key, Value>> GetAllKeyValuePairs()
    {
        for (uint i = 0; i < capacity; i++)
        {
            if (values[i] != null)
            {
                yield return new KeyValuePair<Key, Value>(keys[i], values[i]);
            }
        }
    }

    /// <summary>
    /// Returns the index of the first empty slot in the array. Returns `uint.MaxValue` if no empty slot is found.
    /// </summary>
    public uint GetFirstEmptyArraySlotIndex()
    {
        for (uint i = 0; i < capacity; i++)
        {
            if (values[i] == null)
            {
                return i;
            }
        }
        return uint.MaxValue; // No empty slot found
    }
}
