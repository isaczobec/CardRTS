using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;

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

        _driver = CreateDriver(null);
        _connection = _driver.Connect(endpoint);
        return true;
    }

    // Connects through a Unity Relay allocation instead of a direct IP, so a host behind
    // NAT/CGNAT can be reached without port forwarding. relayServerData comes from
    // RelayNetworkService.JoinAllocationAsync. Unlike the direct-IP path, Connect() doesn't
    // implicitly bind a local socket for relay traffic, so it's done explicitly here first.
    public bool ConnectRelay(RelayServerData relayServerData)
    {
        _driver = CreateDriver(relayServerData);

        if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
        {
            DevConsole.LogError("[Net] Relay client bind failed.");
            _driver.Dispose();
            return false;
        }

        _connection = _driver.Connect(relayServerData.Endpoint);
        return true;
    }

    NetworkDriver CreateDriver(RelayServerData? relayServerData)
    {
        var settings = new NetworkSettings();
        settings.WithNetworkConfigParameters(receiveQueueCapacity: 1024, sendQueueCapacity: 1024);
        // Must match the server's pipeline stages/config — see NetworkServer.CreateDriver.
        settings.WithFragmentationStageParameters(payloadCapacity: 256 * 1024);
        settings.WithReliableStageParameters(windowSize: 256);

        if (relayServerData.HasValue)
        {
            var relay = relayServerData.Value;
            settings.WithRelayParameters(ref relay);
        }

        var driver = NetworkDriver.Create(settings);
        _reliable = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        return driver;
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

        int beginResult = _driver.BeginSend(_reliable, _connection, out var writer);
        if (beginResult != 0)
        {
            DebugLogger.LogError($"[Net] BeginSend failed ({(Unity.Networking.Transport.Error.StatusCode)beginResult}) sending {data.Length} bytes.");
            return;
        }

        var native = new NativeArray<byte>(data, Allocator.Temp);
        writer.WriteBytes(native);
        native.Dispose();
        int endResult = _driver.EndSend(writer);
        if (endResult < 0)
            DebugLogger.LogError($"[Net] EndSend failed ({(Unity.Networking.Transport.Error.StatusCode)endResult}) sending {data.Length} bytes.");
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
