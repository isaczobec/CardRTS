using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class NetworkManager : Singleton<NetworkManager>
{
    public Queue<InboundMessage> InboundQueue { get; } = new Queue<InboundMessage>();

    NetworkServer _server;
    NetworkClient _client;

    public bool IsServer => _server != null;
    public bool IsClient => _client != null;

    protected override void Awake()
    {
        base.Awake();
    }

    void Start()
    {
        RegisterCommands();
    }

    void Update()
    {
        _server?.Tick();
        _client?.Tick();
    }

    void OnDestroy()
    {
        _server?.Dispose();
        _client?.Dispose();
    }

    public void StartServer(ushort port = 9000)
    {
        if (_server != null)
        {
            DevConsole.LogWarning("[Net] Server already running.");
            return;
        }
        _server = new NetworkServer(InboundQueue);
        if (_server.Start(port))
            DevConsole.LogInfo($"[Net] Server started on port {port}.");
        else
        {
            DevConsole.LogError($"[Net] Failed to start server on port {port}.");
            _server = null;
        }
    }

    public void ConnectToServer(string host, ushort port = 9000)
    {
        if (_client != null)
        {
            DevConsole.LogWarning("[Net] Client already connected or connecting.");
            return;
        }
        _client = new NetworkClient(InboundQueue);
        if (_client.Connect(host, port))
            DevConsole.LogInfo($"[Net] Connecting to {host}:{port}...");
        else
            _client = null;
    }

    public void SendToAll(byte[] data) => _server?.SendToAll(data);
    public void SendToServer(byte[] data) => _client?.Send(data);

    static byte[] BuildHelloMessage(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] message = new byte[1 + payload.Length];
        message[0] = (byte)MessageType.TestHello;
        payload.CopyTo(message, 1);
        return message;
    }

    void RegisterCommands()
    {
        DevConsole.RegisterCommand(
            "net-start-server",
            "Starts a server. Usage: net-start-server [port]",
            info =>
            {
                ushort port = 9000;
                if (info.positionalArgs.Length > 0 && !ushort.TryParse(info.positionalArgs[0], out port))
                    return DevCommandResult.Error("Invalid port number.");
                StartServer(port);
                return DevCommandResult.Success();
            });

        DevConsole.RegisterCommand(
            "net-connect",
            "Connects to a server. Usage: net-connect [host] [port]",
            info =>
            {
                string host = info.positionalArgs.Length > 0 ? info.positionalArgs[0] : "127.0.0.1";
                ushort port = 9000;
                if (info.positionalArgs.Length > 1 && !ushort.TryParse(info.positionalArgs[1], out port))
                    return DevCommandResult.Error("Invalid port number.");
                ConnectToServer(host, port);
                return DevCommandResult.Success();
            });

        DevConsole.RegisterCommand(
            "net-send-hello",
            "Sends a TestHello message. Server broadcasts to all clients; client sends to server. Usage: net-send-hello <message>",
            info =>
            {
                if (info.positionalArgs.Length == 0)
                    return DevCommandResult.Error("Usage: net-send-hello <message>");

                string text = string.Join(" ", info.positionalArgs);
                byte[] msg = BuildHelloMessage(text);

                if (IsServer)
                    SendToAll(msg);
                else if (IsClient)
                    SendToServer(msg);
                else
                    return DevCommandResult.Error("Not running as server or client.");

                return DevCommandResult.Success();
            });

        DevConsole.RegisterCommand(
            "net-status",
            "Shows current network status.",
            _ =>
            {
                if (IsServer)
                    return DevCommandResult.Success($"Server running. Connections: {_server.ConnectionCount}");
                if (IsClient)
                    return DevCommandResult.Success($"Client. Connected: {_client.IsConnected}");
                return DevCommandResult.Success("Not running.");
            });
    }
}
