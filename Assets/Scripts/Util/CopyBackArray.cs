using System.Collections;
using System.Collections.Generic;

public class CopyBackArray<T> : IEnumerable<T> where T : struct
{

    public readonly struct RemovalResult
    {
        public readonly bool movedValid;
        public readonly bool removedValid;
        public readonly uint removedIndex;
        public readonly uint movedIndex;

        public RemovalResult(uint removedIndex, bool removedValid, uint movedIndex, bool movedValid)
        {
            this.removedIndex = removedIndex;
            this.removedValid = removedValid;
            this.movedIndex = movedIndex;
            this.movedValid = movedValid;
        }
    }

    public readonly struct AddResult
    {
        public readonly uint addedIndex;
        public readonly bool addedValid;
        public AddResult(uint addedIndex, bool addedValid)
        {
            this.addedIndex = addedIndex;
            this.addedValid = addedValid;
        }
    }

    private T[] _buffer;
    private uint _size;
    public uint Size => _size;
    private uint _capacity;

    public CopyBackArray(uint capacity)
    {
        _buffer = new T[capacity];
        _size = 0;
        _capacity = capacity;
    }

    public RemovalResult Remove(uint index)
    {
        if (_size == 0 || index >= _size)
        {
            DebugLogger.LogWarning($"index {index} was out of range for CopyBackArray with size {_size}!");
            return new RemovalResult(default, false, default, false);
        }

        T removed = _buffer[index];
        uint lastIndex = _size - 1;
        _buffer[index] = _buffer[lastIndex];
        _size -= 1;

        bool moved = index != lastIndex;
        return new RemovalResult(index, true, lastIndex, moved);
    }
    
    public AddResult Add(T item)
    {
        if (_size >= _capacity)
        {
            DebugLogger.LogError($"Capacity of {_capacity} exceeded for CopyBackArray!");
            return new AddResult(default, false);
        }

        _buffer[_size++] = item;
        return new AddResult(_size-1, true);
    } 

    public T this[int index]
    {
        get => _buffer[index];
        set => _buffer[index] = value;
    }
    public ref T GetRef(uint index) => ref _buffer[index];

    public IEnumerator<T> GetEnumerator()
    {
        for (uint i = 0; i < _size; i++)
            yield return _buffer[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

}