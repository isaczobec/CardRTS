using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class NetworkManager : Singleton<NetworkManager>
{
    public Queue<InboundMessage> InboundQueue { get; } = new Queue<InboundMessage>();
    public NetworkContext Context { get; } = new NetworkContext();

    NetworkServer _server;
    NetworkClient _client;

    public bool IsServer => Context.IsServer;
    public bool IsClient => Context.IsClient;

    private int _readyClientCount = 0;
    public bool GameStarted { get; private set; } = false;

    private ComponentDeltaManager _clientDeltaManager;

    protected override void Awake()
    {
        base.Awake();
    }

    void Start()
    {
        RegisterCommands();
        TickManager.instance.AfterTick += OnAfterTick;
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
        {
            Context.AddRole(NetworkRole.Server);
            DevConsole.LogInfo($"[Net] Server started on port {port}.");
        }
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
        {
            Context.AddRole(NetworkRole.Client);
            DevConsole.LogInfo($"[Net] Connecting to {host}:{port}...");
        }
        else
            _client = null;
    }

    public void SendToAll(byte[] data) => _server?.SendToAll(data);
    public void SendToServer(byte[] data) => _client?.Send(data);

    // Called by TickManager.AfterTick — broadcasts the simulation delta to all clients after each tick.
    void OnAfterTick()
    {
        if (!IsServer || !GameStarted) return;
        SendToAll(BuildSimulationDeltaMessage(TickManager.instance.Tick, TickManager.instance.ECS, TickManager.instance.LastTickFlagEvents));
    }

    // Called by MessageConsumer when a GameStart message is received on a client.
    public void OnGameStart(byte[] data)
    {
        if (!IsServer)
        {
            TickManager.instance.SetupClientECS();
            _clientDeltaManager = new ComponentDeltaManager(TickManager.instance.ClientServerMirrorECS, TickManager.instance.ComponentTypeRegistry);

            var (created, deleted, deletedComp, compDelta) = ReadDeltaStreams(data, 1);
            _clientDeltaManager.ApplyDelta(TickManager.instance.ClientServerMirrorECS, created, deleted, deletedComp, compDelta);

            DevConsole.LogInfo("[Net] GameStart received. Local and server mirror ECS ready.");
        }

        SendToServer(new byte[] { (byte)MessageType.ClientReady });
        DevConsole.LogInfo("[Net] Sent ClientReady.");
    }

    // Called by MessageConsumer on the server when a ClientReady arrives.
    public void NotifyClientReady()
    {
        _readyClientCount++;
        int total = _server.ConnectionCount;
        DevConsole.LogInfo($"[Net] Client ready ({_readyClientCount}/{total}).");
        if (_readyClientCount >= total && total > 0)
        {
            GameStarted = true;
            SendToAll(new byte[] { (byte)MessageType.GameReady });
            TickManager.instance.StartGame();
            DevConsole.LogInfo("[Net] All clients ready. GameReady sent. Simulation started.");
        }
    }

    // Called by MessageConsumer when GameReady is received on a client.
    public void OnGameReady()
    {
        TickManager.instance.StartGame();
        DevConsole.LogInfo("[Net] GameReady received. Client simulation started.");
    }

    // Called by MessageConsumer when a SimulationDelta arrives on a client.
    // Wire layout: [type:byte][tick:ulong][serverTime:double][...delta streams...]
    public void OnSimulationDelta(byte[] data)
    {
        if (TickManager.instance.ClientServerMirrorECS == null || _clientDeltaManager == null) return;

        using var ms = new MemoryStream(data, 1, data.Length - 1);
        using var reader = new BinaryReader(ms);
        ulong serverTick   = reader.ReadUInt64();
        double serverTime  = reader.ReadDouble();
        byte[] flagEvents  = reader.ReadBytes(reader.ReadInt32());
        byte[] created     = reader.ReadBytes(reader.ReadInt32());
        byte[] deleted     = reader.ReadBytes(reader.ReadInt32());
        byte[] deletedComp = reader.ReadBytes(reader.ReadInt32());
        byte[] compDelta   = reader.ReadBytes(reader.ReadInt32());

        _clientDeltaManager.ApplyDelta(TickManager.instance.ClientServerMirrorECS, created, deleted, deletedComp, compDelta);

        TickManager.instance.ServerFlagEvents.AddFromBytes(flagEvents, TickManager.instance.FlagEventTypeRegistry);
        TickManager.instance.ServerFlagEvents.Flush();

        AdjustClientTickRate(serverTick, serverTime);
    }

    void AdjustClientTickRate(ulong serverTick, double serverTime)
    {
        // Estimate which tick the server is on right now by accounting for time elapsed
        // since the delta was produced. This assumes server and client Unity clocks advance
        // at the same rate (true on LAN / same machine); the absolute offset is irrelevant
        // because we only care about the delta relative to our own local time.
        double elapsedSinceServerTick = Time.timeAsDouble - serverTime;
        double estimatedServerTickNow = serverTick + elapsedSinceServerTick / TickManager.TickInterval;

        long tickError = Mathf.RoundToInt((float)estimatedServerTickNow) - (long)TickManager.instance.Tick;

        // Proportional control: 1 tick of error → 10% speed adjustment, capped at ±50%.
        float scale = 1f + Mathf.Clamp(tickError * 0.1f, -0.5f, 0.5f);
        TickManager.instance.SetTickRateScale(scale);
    }

    static byte[] BuildHelloMessage(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] message = new byte[1 + payload.Length];
        message[0] = (byte)MessageType.TestHello;
        payload.CopyTo(message, 1);
        return message;
    }

    static byte[] BuildGameStartMessage(ECS ecs)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((byte)MessageType.GameStart);
        WriteDeltaStreams(writer, ecs);
        return ms.ToArray();
    }

    static byte[] BuildSimulationDeltaMessage(ulong tick, ECS ecs, byte[] flagEvents)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((byte)MessageType.SimulationDelta);
        writer.Write(tick);
        writer.Write(Time.timeAsDouble);
        WriteLengthPrefixed(writer, flagEvents);
        WriteDeltaStreams(writer, ecs);
        return ms.ToArray();
    }

    static void WriteDeltaStreams(BinaryWriter writer, ECS ecs)
    {
        WriteLengthPrefixed(writer, ecs.Delta.GetCreatedEntities());
        WriteLengthPrefixed(writer, ecs.Delta.GetDeletedEntities());
        WriteLengthPrefixed(writer, ecs.Delta.GetDeletedComponents());
        WriteLengthPrefixed(writer, ecs.Delta.GetComponentsDelta());
    }

    static void WriteLengthPrefixed(BinaryWriter writer, byte[] data)
    {
        writer.Write(data.Length);
        writer.Write(data);
    }

    static (byte[] created, byte[] deleted, byte[] deletedComp, byte[] compDelta) ReadDeltaStreams(byte[] data, int offset)
    {
        using var ms = new MemoryStream(data, offset, data.Length - offset);
        using var reader = new BinaryReader(ms);
        byte[] created    = reader.ReadBytes(reader.ReadInt32());
        byte[] deleted    = reader.ReadBytes(reader.ReadInt32());
        byte[] deletedComp = reader.ReadBytes(reader.ReadInt32());
        byte[] compDelta  = reader.ReadBytes(reader.ReadInt32());
        return (created, deleted, deletedComp, compDelta);
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

        DevConsole.RegisterCommand(
            "net-start-game",
            "Server broadcasts GameStart with initial state and waits for all clients to respond ready.",
            _ =>
            {
                if (!IsServer)
                    return DevCommandResult.Error("Only the server can start the game.");
                if (GameStarted)
                    return DevCommandResult.Error("Game already started.");

                _readyClientCount = 0;
                SendToAll(BuildGameStartMessage(TickManager.instance.ECS));

                int clientCount = _server.ConnectionCount;
                DevConsole.LogInfo($"[Net] GameStart sent to {clientCount} client(s). Waiting for ready...");

                if (clientCount == 0)
                {
                    GameStarted = true;
                    TickManager.instance.StartGame();
                    DevConsole.LogInfo("[Net] No clients connected. Game started immediately.");
                }

                return DevCommandResult.Success();
            });
    }
}
