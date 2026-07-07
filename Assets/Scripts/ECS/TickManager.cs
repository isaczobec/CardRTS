using System;
using System.Collections.Generic;
using UnityEngine;

public class TickManager : Singleton<TickManager>
{
    // Authoritative simulation — ticked on server and standalone.
    public ECS ECS { get; private set; }
    // Local prediction ECS — ticked on clients and host.
    public ECS ClientLocalECS { get; private set; }
    // Mirror of server state — updated only via incoming deltas, never ticked.
    public ECS ClientServerMirrorECS { get; private set; }

    public FlagEventManager FlagEvents => ECS.FlagEvents;
    // Clients and host use ClientLocalECS for visuals; server/standalone fall back to ECS.
    public ECS ActiveECS => ClientLocalECS ?? ECS;
    // Receives deserialized server flag events on clients/host.
    public FlagEventManager ServerFlagEvents { get; } = new FlagEventManager();
    // Serialized flag events from the last server tick, bundled into the outgoing SimulationDelta.
    public byte[] LastTickFlagEvents { get; private set; } = Array.Empty<byte>();

    private TypeRegistry<IComponent> _componentTypeRegistry;
    public TypeRegistry<IComponent> ComponentTypeRegistry => _componentTypeRegistry;

    private readonly TypeRegistry<FlagEvent> _flagEventTypeRegistry = new();
    public TypeRegistry<FlagEvent> FlagEventTypeRegistry => _flagEventTypeRegistry;

    private readonly TypeRegistry<InputBase> _inputTypeRegistry = new();
    public TypeRegistry<InputBase> InputTypeRegistry => _inputTypeRegistry;

    public const float TickInterval = 0.1f;

    private ulong _tick;       // prediction tick — timer-driven, used by ClientLocalECS and clients
    private ulong _serverTick; // authoritative tick — lockstep-driven, used by server ECS

    public ulong Tick       => _tick;
    public ulong ServerTick => _serverTick;
    public float TimeSinceLastTick => _timer;

    private float _timer;
    private bool _gameStarted = false;

    // Fires after each authoritative server tick (and after standalone ticks).
    public Action AfterServerTick;

    private float _tickRateScale = 1f;
    public void SetTickRateScale(float scale) =>
        _tickRateScale = Mathf.Clamp(scale, 0.5f, 2f);

    private ComponentDeltaManager _clientDeltaManager;

    private struct PendingDelta
    {
        public ulong ServerTick;
        public byte[] FlagEvents;
        public byte[] Created;
        public byte[] Deleted;
        public byte[] DeletedComp;
        public byte[] CompDelta;
        // When true the mirror was already copied directly; skip ApplyDelta.
        public bool IsDirect;
    }

    private readonly Queue<PendingDelta> _pendingDeltas = new();

    // Used by pure clients receiving SimulationDelta messages over the network.
    public void SetPendingServerDelta(ulong serverTick, byte[] flagEvents,
        byte[] created, byte[] deleted, byte[] deletedComp, byte[] compDelta)
    {
        _pendingDeltas.Enqueue(new PendingDelta
        {
            ServerTick  = serverTick,
            FlagEvents  = flagEvents,
            Created     = created,
            Deleted     = deleted,
            DeletedComp = deletedComp,
            CompDelta   = compDelta,
            IsDirect    = false,
        });
    }

    // Used by the host after each authoritative tick.
    // Copies ECS state directly into the mirror and queues a reconcile trigger.
    public void SetPendingServerStateDirectly(ulong serverTick)
    {
        if (ClientServerMirrorECS == null || ClientLocalECS == null) return;
        ClientServerMirrorECS.CopyStateFrom(ECS);
        _pendingDeltas.Enqueue(new PendingDelta
        {
            ServerTick  = serverTick,
            FlagEvents  = LastTickFlagEvents,
            IsDirect    = true,
            Created     = Array.Empty<byte>(),
            Deleted     = Array.Empty<byte>(),
            DeletedComp = Array.Empty<byte>(),
            CompDelta   = Array.Empty<byte>(),
        });
    }

    protected override void Awake()
    {
        base.Awake();
        _tick = 0;
        _serverTick = 0;

        _componentTypeRegistry = new TypeRegistry<IComponent>();
        _componentTypeRegistry.Register<PositionComponent>(0);
        _componentTypeRegistry.Register<RandomWalkComponent>(1);
        _componentTypeRegistry.Register<PlayerComponent>(2);
        _componentTypeRegistry.Register<TroopComponent>(3);
        _componentTypeRegistry.Register<RenderableComponent>(4);
        _componentTypeRegistry.Register<SelectableComponent>(5);
        _componentTypeRegistry.Register<MovableComponent>(6);

        _inputTypeRegistry.Register<SpawnEntityInput>(0);
        _inputTypeRegistry.Register<MoveInput>(1);
        _inputTypeRegistry.Register<SpawnTroopInput>(2);
        _inputTypeRegistry.Register<MoveTroopInput>(3);

        _flagEventTypeRegistry.Register<EntityCreatedEvent>(0);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PositionComponent>>(1);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RandomWalkComponent>>(2);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PlayerComponent>>(4);
        _flagEventTypeRegistry.Register<PositionUpdatedEvent>(3);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<TroopComponent>>(5);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RenderableComponent>>(6);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<SelectableComponent>>(7);
        _flagEventTypeRegistry.Register<EntityDeletedEvent>(8);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<PositionComponent>>(9);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<RandomWalkComponent>>(10);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<PlayerComponent>>(11);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<TroopComponent>>(12);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<RenderableComponent>>(13);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<SelectableComponent>>(14);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<MovableComponent>>(15);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<MovableComponent>>(16);

        ECS = CreateSimulationECS();
    }

    // Called on a client when GameStart is received — before the game begins ticking.
    // Also called on the host from NetworkManager before StartGame().
    public void SetupClientECS()
    {
        ClientLocalECS = CreateSimulationECS();
        ClientServerMirrorECS = CreateMirrorECS();
        _clientDeltaManager = new ComponentDeltaManager(ClientServerMirrorECS, _componentTypeRegistry);
    }

    public void StartGame()
    {
        _gameStarted = true;
        RenderingSetup.instance.SetupRendering();
        GameEvents.FireGameStarting();
    }

    void Update()
    {
        if (!_gameStarted) return;

        bool isServer = NetworkManager.instance != null && NetworkManager.instance.IsServer;
        bool isClient = NetworkManager.instance != null && NetworkManager.instance.IsClient;

        // Prediction tick — fires on timer for all clients and the host.
        // Also handles standalone (no networking) directly through the ECS.
        _timer += Time.deltaTime;
        float effectiveInterval = TickInterval / _tickRateScale;
        if (_timer >= effectiveInterval)
        {
            _timer -= effectiveInterval;
            DoPredictionTick(isServer, isClient);
        }

        // Server (authoritative) tick — driven by lockstep: fires as soon as all
        // connected clients (and the host) have sent their inputs for _serverTick.
        // NOTE: for best results set this script to execute AFTER NetworkManager
        // and MessageConsumer in Unity's Script Execution Order settings so that
        // incoming ClientTickInput messages are processed before TryRunServerTick.
        if (isServer)
            TryRunServerTick();
    }

    void DoPredictionTick(bool isServer, bool isClient)
    {
        bool isPureClient = isClient && !isServer;
        bool isStandalone = !isServer && !isClient;

        // Standalone path: run ECS directly with no prediction.
        if (isStandalone)
        {
            ECS.CurrentSimulationTick = _tick;
            ECS.ExecuteSystems();
            LastTickFlagEvents = Array.Empty<byte>();
            ECS.FlagEvents.Flush();
            ECS.Delta.DispatchComponentChangedEvents();
            _serverTick = _tick;
            _tick++;
            AfterServerTick?.Invoke();
            return;
        }

        // Prediction for clients and host.
        if (ClientLocalECS != null)
        {
            if (_pendingDeltas.Count > 0)
                RunReconciliation();

            ClientLocalECS.CurrentSimulationTick = _tick;
            ClientLocalECS.ExecuteSystems();
            ClientLocalECS.FlagEvents.Flush();
            ClientLocalECS.Delta.DispatchComponentChangedEvents();

            // Guarantee an InputBuffer entry so the server can attach remote inputs later.
            InputBuffer.EnsureTickEntry(_tick);
        }

        if (isPureClient)
            NetworkManager.instance?.SendClientTickInput(_tick);
        else if (isServer && ClientLocalECS != null)
            NetworkManager.instance?.NotifyHostTickReady(_tick);

        _tick++;
    }

    // Called every frame for the server. Runs as many authoritative ticks as
    // possible given what all clients have sent (lockstep).
    void TryRunServerTick()
    {
        const int maxCatchup = 20;
        int ran = 0;
        while (ran++ < maxCatchup && NetworkManager.instance.IsAllClientsReadyForTick(_serverTick))
        {
            NetworkManager.instance.ApplyClientInputsForTick(_serverTick);

            ECS.CurrentSimulationTick = _serverTick;
            ECS.ExecuteSystems();
            LastTickFlagEvents = ECS.FlagEvents.SerializePending(_flagEventTypeRegistry);
            ECS.FlagEvents.Flush();
            ECS.Delta.DispatchComponentChangedEvents();

            _serverTick++;
            AfterServerTick?.Invoke();

            // Queue reconciliation for the host's prediction ECS.
            if (ClientLocalECS != null)
                SetPendingServerStateDirectly(_serverTick); // post-increment
        }
    }

    void RunReconciliation()
    {
        ulong newestServerTick = 0;

        while (_pendingDeltas.TryDequeue(out PendingDelta delta))
        {
            if (!delta.IsDirect)
                _clientDeltaManager.ApplyDelta(ClientServerMirrorECS,
                    delta.Created, delta.Deleted, delta.DeletedComp, delta.CompDelta);
            // IsDirect: mirror was already set by SetPendingServerStateDirectly; nothing to do.
            ServerFlagEvents.AddFromBytes(delta.FlagEvents, _flagEventTypeRegistry);
            if (delta.ServerTick > newestServerTick)
                newestServerTick = delta.ServerTick;
        }

        ClientLocalECS.CopyStateFrom(ClientServerMirrorECS);
        ServerFlagEvents.Flush();

        ulong ticksToReplay = _tick > newestServerTick ? _tick - newestServerTick : 0;
        for (ulong i = 0; i < ticksToReplay; i++)
        {
            ClientLocalECS.CurrentSimulationTick = newestServerTick + i;
            ClientLocalECS.ExecuteSystems();
            ClientLocalECS.FlagEvents.Flush();
        }
    }

    private ECS CreateSimulationECS()
    {
        var ecs = new ECS();
        ecs.AddComponentStore(new ComponentStore<PositionComponent>());
        ecs.AddComponentStore(new ComponentStore<RandomWalkComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerComponent>());
        ecs.AddComponentStore(new ComponentStore<TroopComponent>());
        ecs.AddComponentStore(new ComponentStore<RenderableComponent>());
        ecs.AddComponentStore(new ComponentStore<SelectableComponent>());
        ecs.AddComponentStore(new ComponentStore<MovableComponent>());
        // ecs.RegisterSystem(SpawnEntitySystem.Instance);
        ecs.RegisterSystem(SpawnTroopSystem.Instance);
        // ecs.RegisterSystem(PlayerMovementSystem.Instance);
        // ecs.RegisterSystem(RandomWalkSystem.Instance);
        ecs.RegisterSystem(PathfindingSystem.Instance);
        return ecs;
    }

    private ECS CreateMirrorECS()
    {
        var ecs = new ECS();
        ecs.AddComponentStore(new ComponentStore<PositionComponent>());
        ecs.AddComponentStore(new ComponentStore<RandomWalkComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerComponent>());
        ecs.AddComponentStore(new ComponentStore<TroopComponent>());
        ecs.AddComponentStore(new ComponentStore<RenderableComponent>());
        ecs.AddComponentStore(new ComponentStore<SelectableComponent>());
        ecs.AddComponentStore(new ComponentStore<MovableComponent>());

        return ecs;
    }
}
