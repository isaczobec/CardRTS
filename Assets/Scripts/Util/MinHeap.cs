using System;

// Mostly implemented by ChatGPT
public class MinHeap<K, V> where K : IComparable<K>
{
    private readonly K[] keys;
    private readonly V[] values;
    private int count = 0;

    public int Count => count;
    public int Capacity => keys.Length;

    public MinHeap(int maxSize)
    {
        if (maxSize <= 0)
            throw new ArgumentException("Heap size must be positive");
        keys = new K[maxSize];
        values = new V[maxSize];
    }

    public void Add(K item, V value)
    {
        if (count >= keys.Length)
            throw new InvalidOperationException("Heap is full");

        keys[count] = item;
        values[count] = value;
        HeapifyUp(count);
        count++;
    }

    public V Pop()
    {
        if (count == 0)
            throw new InvalidOperationException("Heap is empty");

        V result = values[0];
        keys[0] = keys[count - 1];
        values[0] = values[count - 1];
        count--;
        HeapifyDown(0);
        return result;
    }

    public V Peek()
    {
        if (count == 0)
            throw new InvalidOperationException("Heap is empty");

        return values[0];
    }

    public K PeekKey()
    {
        if (count == 0)
            throw new InvalidOperationException("Heap is empty");

        return keys[0];
    }



    private void HeapifyUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (keys[index].CompareTo(keys[parent]) >= 0)
                break;

            Swap(index, parent);
            index = parent;
        }
    }

    private void HeapifyDown(int index)
    {
        while (true)
        {
            int smallest = index;
            int left = index * 2 + 1;
            int right = index * 2 + 2;

            if (left < count && keys[left].CompareTo(keys[smallest]) < 0)
                smallest = left;
            if (right < count && keys[right].CompareTo(keys[smallest]) < 0)
                smallest = right;

            if (smallest == index)
                break;

            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int a, int b)
    {
        K tmp = keys[a];
        keys[a] = keys[b];
        keys[b] = tmp;

        V tmpValue = values[a];
        values[a] = values[b];
        values[b] = tmpValue;
    }
}
