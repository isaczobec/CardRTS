using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;

public class NetworkServer
{
    NetworkDriver _driver;
    NetworkPipeline _reliable;
    NativeList<NetworkConnection> _connections;
    Queue<InboundMessage> _inboundQueue;
    Dictionary<int, ushort> _connectionToPlayerId = new();
    ushort _nextPlayerId = 1;

    public bool IsRunning => _driver.IsCreated;
    public int ConnectionCount => _connections.IsCreated ? _connections.Length : 0;
    public IEnumerable<ushort> ConnectedPlayerIds => _connectionToPlayerId.Values;

    public NetworkServer(Queue<InboundMessage> inboundQueue)
    {
        _inboundQueue = inboundQueue;
    }

    public bool Start(ushort port)
    {
        // Default queue capacity (512, shared across all connections) is sized for a much
        // lower tick rate than we now run at; the server fans a message out to every
        // connected client each server tick, so give it more headroom than the client.
        var settings = new NetworkSettings();
        settings.WithNetworkConfigParameters(receiveQueueCapacity: 2048, sendQueueCapacity: 2048);

        _driver = NetworkDriver.Create(settings);
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
            ushort playerId = _nextPlayerId++;
            _connectionToPlayerId[incoming.GetHashCode()] = playerId;
            _connections.Add(incoming);
            SendTo(incoming, BuildPlayerIdAssignedMessage(playerId));
            DevConsole.LogInfo($"[Net] Client connected (playerId={playerId}).");
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
                    ushort senderId = _connectionToPlayerId.TryGetValue(_connections[i].GetHashCode(), out var pid) ? pid : (ushort)0;
                    _inboundQueue.Enqueue(new InboundMessage(senderId, bytes.ToArray()));
                    bytes.Dispose();
                }
                else if (evt == NetworkEvent.Type.Disconnect)
                {
                    int hash = _connections[i].GetHashCode();
                    _connectionToPlayerId.TryGetValue(hash, out ushort disconnectedId);
                    _connectionToPlayerId.Remove(hash);
                    DevConsole.LogInfo($"[Net] Client disconnected (playerId={disconnectedId}).");
                    _connections[i] = default;
                }
            }
        }

        for (int i = _connections.Length - 1; i >= 0; i--)
            if (!_connections[i].IsCreated)
                _connections.RemoveAtSwapBack(i);
    }

    void SendTo(NetworkConnection conn, byte[] data)
    {
        var native = new NativeArray<byte>(data, Allocator.Temp);
        _driver.BeginSend(_reliable, conn, out var writer);
        writer.WriteBytes(native);
        _driver.EndSend(writer);
        native.Dispose();
    }

    static byte[] BuildPlayerIdAssignedMessage(ushort playerId)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms);
        writer.Write((byte)MessageType.PlayerIdAssigned);
        writer.Write(playerId);
        return ms.ToArray();
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
