using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;

public class NetworkClient
{
    NetworkDriver _driver;
    NetworkPipeline _reliable;
    NetworkConnection _connection;
    Queue<InboundMessage> _inboundQueue;

    public bool IsConnected => _connection.IsCreated;

    public NetworkClient(Queue<InboundMessage> inboundQueue)
    {
        _inboundQueue = inboundQueue;
    }

    public bool Connect(string host, ushort port)
    {
        if (!NetworkEndpoint.TryParse(host, port, out var endpoint))
        {
            DevConsole.LogError($"[Net] Invalid address: {host}:{port}");
            return false;
        }

        var settings = new NetworkSettings();
        settings.WithNetworkConfigParameters(receiveQueueCapacity: 1024, sendQueueCapacity: 1024);

        _driver = NetworkDriver.Create(settings);
        _reliable = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
        _connection = _driver.Connect(endpoint);
        return true;
    }

    public void Tick()
    {
        if (!_driver.IsCreated) return;

        _driver.ScheduleUpdate().Complete();

        NetworkEvent.Type evt;
        while ((evt = _driver.PopEventForConnection(_connection, out var stream)) != NetworkEvent.Type.Empty)
        {
            if (evt == NetworkEvent.Type.Connect)
            {
                DevConsole.LogInfo("[Net] Connected to server.");
            }
            else if (evt == NetworkEvent.Type.Data)
            {
                var bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
                stream.ReadBytes(bytes);
                _inboundQueue.Enqueue(new InboundMessage(0, bytes.ToArray()));
                bytes.Dispose();
            }
            else if (evt == NetworkEvent.Type.Disconnect)
            {
                DevConsole.LogInfo("[Net] Disconnected from server.");
                _connection = default;
            }
        }
    }

    public void Send(byte[] data)
    {
        if (!_connection.IsCreated) return;
        _driver.BeginSend(_reliable, _connection, out var writer);
        var native = new NativeArray<byte>(data, Allocator.Temp);
        writer.WriteBytes(native);
        native.Dispose();
        _driver.EndSend(writer);
    }

    public void Dispose()
    {
        if (_driver.IsCreated)
        {
            _driver.ScheduleUpdate().Complete();
            _driver.Dispose();
        }
    }
}
