using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;

public class NetworkServer
{
    NetworkDriver _driver;
    NetworkPipeline _reliable;
    NativeList<NetworkConnection> _connections;
    Queue<InboundMessage> _inboundQueue;

    public bool IsRunning => _driver.IsCreated;
    public int ConnectionCount => _connections.IsCreated ? _connections.Length : 0;

    public NetworkServer(Queue<InboundMessage> inboundQueue)
    {
        _inboundQueue = inboundQueue;
    }

    public bool Start(ushort port)
    {
        _driver = NetworkDriver.Create();
        _reliable = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
        _connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

        var endpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
        if (_driver.Bind(endpoint) != 0)
        {
            _driver.Dispose();
            return false;
        }

        _driver.Listen();
        return true;
    }

    public void Tick()
    {
        if (!_driver.IsCreated) return;

        _driver.ScheduleUpdate().Complete();

        NetworkConnection incoming;
        while ((incoming = _driver.Accept()) != default)
        {
            _connections.Add(incoming);
            DevConsole.LogInfo($"[Net] Client connected (id={incoming.GetHashCode()}).");
        }

        for (int i = 0; i < _connections.Length; i++)
        {
            NetworkEvent.Type evt;
            while ((evt = _driver.PopEventForConnection(_connections[i], out var stream)) != NetworkEvent.Type.Empty)
            {
                if (evt == NetworkEvent.Type.Data)
                {
                    var bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
                    stream.ReadBytes(bytes);
                    _inboundQueue.Enqueue(new InboundMessage(_connections[i].GetHashCode(), bytes.ToArray()));
                    bytes.Dispose();
                }
                else if (evt == NetworkEvent.Type.Disconnect)
                {
                    DevConsole.LogInfo($"[Net] Client disconnected (id={_connections[i].GetHashCode()}).");
                    _connections[i] = default;
                }
            }
        }

        for (int i = _connections.Length - 1; i >= 0; i--)
            if (!_connections[i].IsCreated)
                _connections.RemoveAtSwapBack(i);
    }

    public void SendToAll(byte[] data)
    {
        if (!_driver.IsCreated) return;
        var native = new NativeArray<byte>(data, Allocator.Temp);
        for (int i = 0; i < _connections.Length; i++)
        {
            if (!_connections[i].IsCreated) continue;
            _driver.BeginSend(_reliable, _connections[i], out var writer);
            writer.WriteBytes(native);
            _driver.EndSend(writer);
        }
        native.Dispose();
    }

    public void Dispose()
    {
        if (_driver.IsCreated)
        {
            _driver.ScheduleUpdate().Complete();
            _driver.Dispose();
        }
        if (_connections.IsCreated) _connections.Dispose();
    }
}
