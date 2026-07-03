using System;
using System.Collections;
using System.Collections.Generic;

// Growable binary min-heap. Dequeue always returns the smallest item, per the
// comparer supplied at construction (or T's natural ordering if none is given).
public class PriorityQueue<T> : IEnumerable<T>
{
    private readonly List<T> _items = new List<T>();
    private readonly IComparer<T> _comparer;

    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;

    public PriorityQueue() : this(Comparer<T>.Default) { }

    public PriorityQueue(IComparer<T> comparer)
    {
        _comparer = comparer ?? Comparer<T>.Default;
    }

    public void Enqueue(T item)
    {
        _items.Add(item);
        HeapifyUp(_items.Count - 1);
    }

    public T Dequeue()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("PriorityQueue is empty.");

        T root = _items[0];
        int last = _items.Count - 1;
        _items[0] = _items[last];
        _items.RemoveAt(last);
        if (_items.Count > 0)
            HeapifyDown(0);
        return root;
    }

    public bool TryDequeue(out T item)
    {
        if (_items.Count == 0)
        {
            item = default;
            return false;
        }
        item = Dequeue();
        return true;
    }

    public T Peek()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("PriorityQueue is empty.");
        return _items[0];
    }

    public void Clear() => _items.Clear();

    private void HeapifyUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_comparer.Compare(_items[index], _items[parent]) >= 0)
                break;
            Swap(index, parent);
            index = parent;
        }
    }

    private void HeapifyDown(int index)
    {
        int count = _items.Count;
        while (true)
        {
            int smallest = index;
            int left = index * 2 + 1;
            int right = index * 2 + 2;

            if (left < count && _comparer.Compare(_items[left], _items[smallest]) < 0)
                smallest = left;
            if (right < count && _comparer.Compare(_items[right], _items[smallest]) < 0)
                smallest = right;

            if (smallest == index)
                break;

            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int a, int b)
    {
        (_items[a], _items[b]) = (_items[b], _items[a]);
    }

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
