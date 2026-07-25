using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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

    // 0 = server/host. Pure clients receive their ID via PlayerIdAssigned.
    public ushort LocalPlayerId { get; private set; } = 0;

    private int _readyClientCount = 0;
    public bool GameStarted { get; private set; } = false;

    // Pending inputs from remote clients, keyed by [tick][clientId].
    private readonly Dictionary<ulong, Dictionary<ushort, List<InputBase>>> _pendingClientInputs = new();
    // Last prediction tick signaled by the host; -1 means not yet started.
    private long _hostReadyTick = -1;


    protected override void Awake()
    {
        base.Awake();
    }

    void Start()
    {
        RegisterCommands();
        TickManager.instance.AfterServerTick += OnAfterServerTick;
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
        if (IsServer)
        {
            // A host never needs a real connection to its own server: its prediction ECS,
            // per-tick readiness, and player id (0, the default) are already wired up
            // directly — see NotifyClientReady/SetupClientECS/SetPendingServerStateDirectly.
            // A real loopback connection would get counted as a second, separate player by
            // NetworkServer's sequential id assignment (on top of the hardcoded id 0 for
            // host), and its PlayerIdAssigned reply would overwrite LocalPlayerId away from
            // the correct 0 — so just skip it.
            DevConsole.LogWarning("[Net] Already running as server — the host doesn't need to connect to itself.");
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

    // ── Lockstep: host and client readiness ──────────────────────────────────

    // Called by the host's TickManager after each prediction tick.
    public void NotifyHostTickReady(ulong predTick)
    {
        _hostReadyTick = (long)predTick;
    }

    // Returns true when all connected remote clients AND the host have submitted
    // their inputs for the given server tick.
    public bool IsAllClientsReadyForTick(ulong tick)
    {
        if (_server == null) return true; // standalone

        foreach (ushort id in _server.ConnectedPlayerIds)
        {
            if ((!_pendingClientInputs.TryGetValue(tick, out var byClient) ||
                !byClient.ContainsKey(id))
                && (!(NetworkManager.instance.IsServer && NetworkManager.instance.LocalPlayerId == id)))
            {
                return false;
            }
        }

        // If the host has prediction set up, it must also have run at least this tick.
        if (TickManager.instance.ClientLocalECS != null)
            return _hostReadyTick >= (long)tick;

        return true;
    }

    // Moves buffered remote-client inputs for the given tick into InputBuffer.
    public void ApplyClientInputsForTick(ulong tick)
    {
        if (!_pendingClientInputs.TryGetValue(tick, out var byClient)) return;
        foreach (var kv in byClient)
            foreach (var input in kv.Value)
                InputBuffer.EnqueueForTick(input, tick);
        _pendingClientInputs.Remove(tick);
    }

    // ── Sending / receiving tick-input bundles ────────────────────────────────

    // Called by TickManager on pure clients after each prediction tick.
    public void SendClientTickInput(ulong tick)
    {
        byte[] inputBytes = InputBuffer.SerializeInputsForTick(tick, TickManager.instance.InputTypeRegistry);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((byte)MessageType.ClientTickInput);
        writer.Write(tick);
        writer.Write(inputBytes.Length);
        writer.Write(inputBytes);

        SendToServer(ms.ToArray());
    }

    // Legacy per-input path (kept for protocol compatibility; no longer the primary path).
    public void OnClientInput(InboundMessage msg)
    {
        using var ms = new MemoryStream(msg.Data, 1, msg.Data.Length - 1);
        using var reader = new BinaryReader(ms);
        reader.ReadUInt64(); // tick (ignored; server uses current tick)
        ushort typeId = reader.ReadUInt16();
        ushort dataLen = reader.ReadUInt16();
        byte[] data = reader.ReadBytes(dataLen);

        Type inputType = TickManager.instance.InputTypeRegistry.GetTypeForID(typeId);
        InputBase input = (InputBase)System.Activator.CreateInstance(inputType);
        input.Deserialize(data);
        input.ClientId = msg.SenderId;
        InputBuffer.EnqueueRaw(input);
    }

    // Called by MessageConsumer on the server when a ClientTickInput message arrives.
    public void OnClientTickInput(InboundMessage msg)
    {
        using var ms = new MemoryStream(msg.Data, 1, msg.Data.Length - 1);
        using var reader = new BinaryReader(ms);
        ulong tick = reader.ReadUInt64();
        int inputBytesLen = reader.ReadInt32();
        byte[] inputBytes = reader.ReadBytes(inputBytesLen);

        var inputs = DeserializeInputs(inputBytes, msg.SenderId);

        if (!_pendingClientInputs.TryGetValue(tick, out var byClient))
            _pendingClientInputs[tick] = byClient = new Dictionary<ushort, List<InputBase>>();
        byClient[msg.SenderId] = inputs;
    }

    private List<InputBase> DeserializeInputs(byte[] data, ushort senderId)
    {
        var result = new List<InputBase>();
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        int count = reader.ReadInt32();
        var registry = TickManager.instance.InputTypeRegistry;
        for (int i = 0; i < count; i++)
        {
            ushort typeId = reader.ReadUInt16();
            ushort dataLen = reader.ReadUInt16();
            byte[] inputData = reader.ReadBytes(dataLen);
            Type inputType = registry.GetTypeForID(typeId);
            InputBase input = (InputBase)Activator.CreateInstance(inputType);
            input.Deserialize(inputData);
            input.ClientId = senderId;
            result.Add(input);
        }
        return result;
    }

    // ── Server → client delta broadcast ──────────────────────────────────────

    void OnAfterServerTick()
    {
        if (!IsServer || !GameStarted) return;
        SendToAll(BuildSimulationDeltaMessage(TickManager.instance.ServerTick,
            TickManager.instance.ECS, TickManager.instance.LastTickFlagEvents));
    }

    // ── Game-start handshake ──────────────────────────────────────────────────

    public void OnGameStart(byte[] data)
    {
        (List<ushort> playerIds, int seed) = ReadGameStartHeader(data, 1, out int deltaOffset);

        if (!IsServer)
        {
            TickManager.instance.SetupClientECS();

            var (created, deleted, deletedComp, compDelta) = ReadDeltaStreams(data, deltaOffset);
            TickManager.instance.SetPendingServerDelta(0, Array.Empty<byte>(), created, deleted, deletedComp, compDelta);

            WorldManager.instance?.GenerateAndRender(playerIds, seed);

            DevConsole.LogInfo($"[Net] GameStart received (seed {seed}). Local and server mirror ECS ready.");
        }

        SendToServer(new byte[] { (byte)MessageType.ClientReady });
        DevConsole.LogInfo("[Net] Sent ClientReady.");
    }

    public void NotifyClientReady()
    {
        _readyClientCount++;
        int total = _server.ConnectionCount;
        DevConsole.LogInfo($"[Net] Client ready ({_readyClientCount}/{total}).");
        if (_readyClientCount >= total && total > 0)
        {
            GameStarted = true;
            SendToAll(new byte[] { (byte)MessageType.GameReady });

            // Set up host prediction before starting the tick loop so that
            // ClientLocalECS is ready on the very first tick.
            TickManager.instance.SetupClientECS();
            TickManager.instance.SetPendingServerStateDirectly(TickManager.instance.ServerTick);

            TickManager.instance.StartGame();
            DevConsole.LogInfo("[Net] All clients ready. GameReady sent. Simulation started.");
        }
    }

    public void OnGameReady()
    {
        TickManager.instance.StartGame();
        DevConsole.LogInfo("[Net] GameReady received. Client simulation started.");
    }

    public void OnPlayerIdAssigned(ushort playerId)
    {
        LocalPlayerId = playerId;
        DevConsole.LogInfo($"[Net] Assigned player ID {playerId}.");
    }

    // Called by MessageConsumer when a SimulationDelta arrives on a pure client.
    // Wire layout: [type:byte][tick:ulong][serverTime:double][flagEventsLen:int][flagEvents][4x delta streams]
    public void OnSimulationDelta(byte[] data)
    {
        // The host's own broadcast loops back to it if it also connected to itself as a
        // client (the normal way to run as host: net-start-server + net-connect
        // 127.0.0.1). The server never needs to reconcile against its own broadcast — it's
        // already the authoritative source, and its own prediction ECS is fed directly by
        // TickManager.SetPendingServerStateDirectly right after each authoritative tick.
        // Without this guard, the same tick's delta (and every flag event in it, e.g.
        // DamageDealtEvent) gets enqueued twice: once directly, once via this handler —
        // RunReconciliation then dispatches each flag event from both copies.
        if (IsServer) return;

        if (TickManager.instance.ClientServerMirrorECS == null) return;

        using var ms = new MemoryStream(data, 1, data.Length - 1);
        using var reader = new BinaryReader(ms);
        ulong serverTick   = reader.ReadUInt64();
        _ = reader.ReadDouble(); // serverTime — part of the wire format, currently unused
        byte[] flagEvents  = reader.ReadBytes(reader.ReadInt32());
        byte[] created     = reader.ReadBytes(reader.ReadInt32());
        byte[] deleted     = reader.ReadBytes(reader.ReadInt32());
        byte[] deletedComp = reader.ReadBytes(reader.ReadInt32());
        byte[] compDelta   = reader.ReadBytes(reader.ReadInt32());

        TickManager.instance.SetPendingServerDelta(serverTick, flagEvents, created, deleted, deletedComp, compDelta);
        // Pure client: keep prediction tick roughly in sync with the server tick just
        // received over the network. The host does the equivalent locally every frame
        // in TickManager.Update (see AdjustTickRateToServerTick), since it never reaches
        // this method (guarded above by IsServer).
        TickManager.instance.AdjustTickRateToServerTick(serverTick);
    }

    // ── Entity spawning ───────────────────────────────────────────────────────

    // 0 (host/standalone) plus every currently connected remote client. Computed once per
    // game start and threaded through SpawnPlayerEntities, WorldManager.GenerateAndRender,
    // and the GameStart message itself, so server and every client agree on exactly the
    // same list — world gen (base placement, tile clearing) depends on it and must be
    // deterministic across machines.
    List<ushort> GetAllPlayerIds()
    {
        var ids = new List<ushort> { 0 };
        if (_server != null)
            ids.AddRange(_server.ConnectedPlayerIds);
        return ids;
    }

    void SpawnPlayerEntities(List<ushort> playerIds)
    {
        ECS ecs = TickManager.instance.ECS;
        foreach (ushort playerId in playerIds)
            SpawnPlayerEntity(ecs, playerId);
    }

    static void SpawnPlayerEntity(ECS ecs, ushort playerId)
    {
        EntityHandle entity = ecs.CreateEntity();
        ecs.AddComponent(entity.Id, new PositionComponent());
        ecs.AddComponent(entity.Id, new PlayerComponent { PlayerId = playerId });
        ecs.AddComponent(entity.Id, new PlayerDeckComponent());
        // Wood/Stone/Metal now come entirely from ResourceProductionOnDeathSystem (killing
        // Tree/Rock/Ore entities) instead of a flat starting rate — only Gold keeps its
        // baseline passive income.
        ecs.AddComponent(entity.Id, new PlayerResourcesComponent() {
            GoldPerSecond = 0f,
            Gold = 10000,
            Wood = 10000,
            Stone = 10000,
            Metal = 10000,
            Gems = 10000,
            Soulstones = 10000,
            });
        // ecs.AddComponent(entity.Id, new PlayerResourcesComponent() {
        //     GoldPerSecond = 0f,
        //     Gold = 50,
        //     Wood = 150,
        //     Stone = 150,
        //     Metal = 150,
        //     });
        ecs.AddComponent(entity.Id, new ShopPurchaseHistoryComponent());

        // Starting decks are intentionally empty — players buy their first cards from the
        // shop instead (see ShopPricingHelper for the discounted early-purchase pricing).
    }

    // ── Message builders ──────────────────────────────────────────────────────

    static byte[] BuildHelloMessage(string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] message = new byte[1 + payload.Length];
        message[0] = (byte)MessageType.TestHello;
        payload.CopyTo(message, 1);
        return message;
    }

    // Wire layout: [type:byte][playerIdCount:int][playerId:ushort]*count][seed:int][4x delta streams]
    // The player id list and world-gen seed travel explicitly (not derived from the delta
    // streams) because a client has no player entities to read them back from yet at the
    // point it needs them — see OnGameStart.
    static byte[] BuildGameStartMessage(ECS ecs, List<ushort> playerIds, int seed)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((byte)MessageType.GameStart);
        writer.Write(playerIds.Count);
        foreach (ushort id in playerIds)
            writer.Write(id);
        writer.Write(seed);
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

    // Reads the [playerIdCount:int][playerId:ushort]*count][seed:int] prefix
    // BuildGameStartMessage writes, and reports where the following delta streams start.
    static (List<ushort> playerIds, int seed) ReadGameStartHeader(byte[] data, int offset, out int nextOffset)
    {
        using var ms = new MemoryStream(data, offset, data.Length - offset);
        using var reader = new BinaryReader(ms);
        int count = reader.ReadInt32();
        var ids = new List<ushort>(count);
        for (int i = 0; i < count; i++)
            ids.Add(reader.ReadUInt16());
        int seed = reader.ReadInt32();
        nextOffset = offset + 4 + count * 2 + 4;
        return (ids, seed);
    }

    static (byte[] created, byte[] deleted, byte[] deletedComp, byte[] compDelta) ReadDeltaStreams(byte[] data, int offset)
    {
        using var ms = new MemoryStream(data, offset, data.Length - offset);
        using var reader = new BinaryReader(ms);
        byte[] created     = reader.ReadBytes(reader.ReadInt32());
        byte[] deleted     = reader.ReadBytes(reader.ReadInt32());
        byte[] deletedComp = reader.ReadBytes(reader.ReadInt32());
        byte[] compDelta   = reader.ReadBytes(reader.ReadInt32());
        return (created, deleted, deletedComp, compDelta);
    }

    // ── Dev console commands ──────────────────────────────────────────────────

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
            "Sends a TestHello message. Usage: net-send-hello <message>",
            info =>
            {
                if (info.positionalArgs.Length == 0)
                    return DevCommandResult.Error("Usage: net-send-hello <message>");
                string text = string.Join(" ", info.positionalArgs);
                byte[] msg = BuildHelloMessage(text);
                if (IsServer) SendToAll(msg);
                else if (IsClient) SendToServer(msg);
                else return DevCommandResult.Error("Not running as server or client.");
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
            "Server spawns entities, sends GameStart to all clients, waits for ready. Usage: net-start-game [seed]",
            info =>
            {
                if (!IsServer)
                    return DevCommandResult.Error("Only the server can start the game.");
                if (GameStarted)
                    return DevCommandResult.Error("Game already started.");

                int seed;
                if (info.positionalArgs.Length > 0)
                {
                    if (!int.TryParse(info.positionalArgs[0], out seed))
                        return DevCommandResult.Error("Invalid seed — must be an integer.");
                }
                else
                {
                    seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                }

                _readyClientCount = 0;
                List<ushort> playerIds = GetAllPlayerIds();
                SpawnPlayerEntities(playerIds);
                WorldManager.instance?.GenerateAndRender(playerIds, seed);
                SendToAll(BuildGameStartMessage(TickManager.instance.ECS, playerIds, seed));

                int clientCount = _server.ConnectionCount;
                DevConsole.LogInfo($"[Net] GameStart sent to {clientCount} client(s) with seed {seed}. Waiting for ready...");

                if (clientCount == 0)
                {
                    GameStarted = true;
                    // Set up host prediction before starting the tick loop.
                    TickManager.instance.SetupClientECS();
                    TickManager.instance.SetPendingServerStateDirectly(TickManager.instance.ServerTick);
                    TickManager.instance.StartGame();
                    DevConsole.LogInfo("[Net] No clients. Game started immediately.");
                }

                return DevCommandResult.Success();
            });
    }
}
