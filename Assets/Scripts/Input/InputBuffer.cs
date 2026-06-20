using System;
using System.Collections.Generic;
using System.Linq;

public static class InputBuffer
{
    public const int INPUTBUFFER_CAPCITY = 1 << 10;
    private static readonly RingBuffer<TickInputStore> _buffer = new(INPUTBUFFER_CAPCITY);

    public static void EnqueueInput<T>(T input) where T : InputBase
    {
        input.ClientId = NetworkManager.instance?.LocalPlayerId ?? 0;

        bool isPureClient = NetworkManager.instance != null
            && NetworkManager.instance.IsClient
            && !NetworkManager.instance.IsServer;
        if (isPureClient)
            NetworkManager.instance.SendInputToServer(input);

        EnqueueRaw(input);
    }

    // Enqueues a pre-constructed input (e.g. received from a remote client) at the current tick.
    public static void EnqueueRaw(InputBase input)
    {
        ulong currentTick = TickManager.instance.Tick;
        if (_buffer.IsEmpty || _buffer.PeekTail().tick != currentTick)
            _buffer.Enqueue(new TickInputStore { tick = currentTick, inputs = new() });
        TickInputStore store = _buffer.PeekTail();
        Type type = input.GetType();
        if (!store.inputs.ContainsKey(type))
            store.inputs[type] = new List<InputBase>();
        store.inputs[type].Add(input);
    }

    public static List<T> GetInputsForTick<T>(ulong tick) where T : InputBase
    {
        // begin looking from the TailIndex
        for (int i = 0; i < _buffer.Count; i++)
        {
            int index = (_buffer.TailIndex - 1 - i + _buffer.Capacity) % _buffer.Capacity;
            TickInputStore store = _buffer[index];
            if (store.tick == tick)
            {
                if (store.inputs.ContainsKey(typeof(T)))
                    return store.inputs[typeof(T)].Cast<T>().ToList();
                return null;
            }
        }
        return null;
    }

    public static List<T> GetHeadInputs<T>() where T : InputBase
    {
        TickInputStore head = _buffer.Peek();
        if (head.inputs.ContainsKey(typeof(T)))
            return head.inputs[typeof(T)].Cast<T>().ToList();
        return null;
    }

}

public class TickInputStore
{
    public ulong tick;
    public Dictionary<Type, List<InputBase>> inputs;
}

public abstract class InputBase
{
    public ushort ClientId { get; set; }
    public abstract byte[] Serialize();
    public abstract void Deserialize(byte[] buffer);
}