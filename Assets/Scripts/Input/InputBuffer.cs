using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class InputBuffer
{
    public const int INPUTBUFFER_CAPCITY = 1 << 10;
    private static readonly RingBuffer<TickInputStore> _buffer = new(INPUTBUFFER_CAPCITY);

    // The buffer models a sliding window of recent ticks. Capacity is far larger than
    // any tick range actually read back (reconciliation replay, tick sends), so once
    // full it's safe to drop the oldest entry to make room for the new one.
    private static void EnqueueTick(TickInputStore store)
    {
        if (_buffer.IsFull)
            _buffer.Dequeue();
        _buffer.Enqueue(store);
    }

    public static void EnqueueInput<T>(T input) where T : InputBase
    {
        if (!ShouldAcceptLocalInput()) return;

        input.ClientId = NetworkManager.instance?.LocalPlayerId ?? 0;
        // Inputs are batched and sent to the server once per prediction tick
        // by TickManager, not immediately here.
        EnqueueRaw(input);
    }

    // Gates local player input only — EnqueueForTick/EnqueueRaw's direct callers apply
    // already-sent remote clients' inputs and must not be affected by our own local UI
    // state. Ignored while the game hasn't started (there's no tick to attach it to yet)
    // or while the dev console is open (typing a command shouldn't also move troops/spawn
    // things underneath it) — or once the local player's own base has died (see
    // PlayerEliminationSystem/PlayerEliminationQuery): ECS.GetInputsForTick would drop it
    // anyway, but stopping the enqueue here gives instant local feedback and skips the wasted
    // network round-trip instead of silently discarding it later.
    private static bool ShouldAcceptLocalInput()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return false;
        if (DevConsole.IsOpen) return false;

        ushort localPlayerId = NetworkManager.instance?.LocalPlayerId ?? 0;
        if (PlayerEliminationQuery.IsEliminated(TickManager.instance.ActiveECS, localPlayerId)) return false;

        return true;
    }

    // Enqueues a pre-constructed input at the current tick.
    public static void EnqueueRaw(InputBase input)
    {
        ulong currentTick = TickManager.instance.Tick;
        if (_buffer.IsEmpty || _buffer.PeekTail().tick != currentTick)
            EnqueueTick(new TickInputStore { tick = currentTick, inputs = new() });
        TickInputStore store = _buffer.PeekTail();
        Type type = input.GetType();
        if (!store.inputs.ContainsKey(type))
            store.inputs[type] = new List<InputBase>();
        store.inputs[type].Add(input);
    }

    // Enqueues an input for a specific tick (used by the server to apply remote client inputs).
    // Searches backward from the tail; if the tick entry doesn't exist yet, adds a new one.
    public static void EnqueueForTick(InputBase input, ulong tick)
    {
        for (int i = 0; i < _buffer.Count; i++)
        {
            int index = _buffer.Count - 1 - i;
            TickInputStore store = _buffer[index];
            if (store.tick == tick)
            {
                Type type = input.GetType();
                if (!store.inputs.ContainsKey(type))
                    store.inputs[type] = new List<InputBase>();
                store.inputs[type].Add(input);
                return;
            }
            if (store.tick < tick) break;
        }
        // Tick not found; add a new entry at the tail (tick must be the next value).
        if (_buffer.IsEmpty || _buffer.PeekTail().tick < tick)
        {
            EnqueueTick(new TickInputStore { tick = tick, inputs = new() });
            var newStore = _buffer.PeekTail();
            Type type = input.GetType();
            newStore.inputs[type] = new List<InputBase> { input };
        }
    }

    // Guarantees a buffer entry exists for the given tick.
    // Called after each prediction tick so server can later attach remote inputs.
    public static void EnsureTickEntry(ulong tick)
    {
        for (int i = 0; i < _buffer.Count; i++)
        {
            int index = _buffer.Count - 1 - i;
            if (_buffer[index].tick == tick) return;
            if (_buffer[index].tick < tick) break;
        }
        if (_buffer.IsEmpty || _buffer.PeekTail().tick < tick)
            EnqueueTick(new TickInputStore { tick = tick, inputs = new() });
    }

    // Serializes all inputs stored for the given tick.
    // Wire format: [count:int][typeId:ushort][dataLen:ushort][data]...
    public static byte[] SerializeInputsForTick(ulong tick, TypeRegistry<InputBase> registry)
    {
        for (int i = 0; i < _buffer.Count; i++)
        {
            int index = _buffer.Count - 1 - i;
            TickInputStore store = _buffer[index];
            if (store.tick == tick)
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                int total = store.inputs.Values.Sum(list => list.Count);
                writer.Write(total);
                foreach (var (type, list) in store.inputs)
                {
                    ushort typeId = registry.GetIDForType(type);
                    foreach (var inp in list)
                    {
                        byte[] data = inp.Serialize();
                        writer.Write(typeId);
                        writer.Write((ushort)data.Length);
                        writer.Write(data);
                    }
                }
                return ms.ToArray();
            }
            if (store.tick < tick) break;
        }
        // No inputs for this tick — return empty count.
        return BitConverter.GetBytes(0);
    }

    public static List<T> GetInputsForTick<T>(ulong tick) where T : InputBase
    {
        for (int i = 0; i < _buffer.Count; i++)
        {
            int index = _buffer.Count - 1 - i;
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
