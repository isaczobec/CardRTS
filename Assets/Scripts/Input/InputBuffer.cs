using System;
using System.Collections.Generic;

public static class InputBuffer
{
    public const int INPUTBUFFER_CAPCITY = 1 << 10; 
    private static RingBuffer<TickInputStore> _buffer;

    public static void EnqueueInput<T>(T input) where T : InputBase
    {
        TickInputStore head = _buffer.Peek();
        ulong currentTick = TickManager.instance.Tick;
        if (head.tick != currentTick)
            _buffer.Enqueue(new TickInputStore() { tick = currentTick, inputs = new() });
        if (!head.inputs.ContainsKey(typeof(T)))
            head.inputs.Add(typeof(T), new());
        head.inputs[typeof(T)].Enqueue(input);
    }
}

public class TickInputStore
{
    public ulong tick;
    public Dictionary<Type, Queue<InputBase>> inputs;
}

public abstract class InputBase
{
    public abstract byte[] Serialize();
    public abstract void Deserialize(byte[] buffer);
}