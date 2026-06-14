using System;
using System.Collections;
using System.Collections.Generic;

public class RingBuffer<T> : IEnumerable<T>
{
    private readonly T[] _buffer;
    private int _head;  // oldest item
    private int _tail;  // next write position
    private int _count;

    public int Count    => _count;
    public int Capacity => _buffer.Length;
    public bool IsEmpty => _count == 0;
    public bool IsFull  => _count == _buffer.Length;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new T[capacity];
    }

    public T Enqueue(T item)
    {
        if (IsFull) throw new InvalidOperationException("RingBuffer is full.");
        _buffer[_tail] = item;
        _tail = (_tail + 1) % _buffer.Length;
        _count++;
        return item;
    }

    public T Dequeue()
    {
        if (IsEmpty) throw new InvalidOperationException("RingBuffer is empty.");
        T item = _buffer[_head];
        _buffer[_head] = default;  // release reference so GC can collect
        _head = (_head + 1) % _buffer.Length;
        _count--;
        return item;
    }

    public T Peek()
    {
        if (IsEmpty) throw new InvalidOperationException("RingBuffer is empty.");
        return _buffer[_head];
    }

    // index 0 = oldest item
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
            return _buffer[(_head + index) % _buffer.Length];
        }
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _head = _tail = _count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _buffer[(_head + i) % _buffer.Length];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
