using System;
using System.Collections.Generic;
using UnityEngine;

public class TickManager : Singleton<TickManager>
{
    // Authoritative simulation — ticked on server and standalone.
    public ECS ECS { get; private set; }
    // Local prediction ECS — ticked on pure clients.
    public ECS ClientLocalECS { get; private set; }
    // Mirror of server state — updated only via incoming deltas, never ticked.
    public ECS ClientServerMirrorECS { get; private set; }

    public FlagEventManager FlagEvents => ECS.FlagEvents;
    // Pure clients use ClientLocalECS; server/standalone use ECS.
    public ECS ActiveECS => (NetworkManager.instance != null && NetworkManager.instance.IsClient && !NetworkManager.instance.IsServer)
        ? ClientLocalECS
        : ECS;
    // Receives deserialized server flag events on clients. Subscribe here to react to server-side events.
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
    private ulong _tick;
    public ulong Tick => _tick;
    public float TimeSinceLastTick => _timer;
    private float _timer;
    private bool _gameStarted = false;
    public Action AfterTick;
    private float _tickRateScale = 1f;

    // scale > 1 ticks faster, scale < 1 ticks slower. Clamped to [0.5, 2].
    public void SetTickRateScale(float scale) =>
        _tickRateScale = Mathf.Clamp(scale, 0.5f, 2f);

    // Client-side delta manager — applies incoming server deltas to ClientServerMirrorECS.
    private ComponentDeltaManager _clientDeltaManager;

    private struct PendingDelta
    {
        public ulong ServerTick;
        public byte[] FlagEvents;
        public byte[] Created;
        public byte[] Deleted;
        public byte[] DeletedComp;
        public byte[] CompDelta;
    }

    private readonly Queue<PendingDelta> _pendingDeltas = new();

    public void SetPendingServerDelta(ulong serverTick, byte[] flagEvents,
        byte[] created, byte[] deleted, byte[] deletedComp, byte[] compDelta)
    {
        _pendingDeltas.Enqueue(new PendingDelta
        {
            ServerTick   = serverTick,
            FlagEvents   = flagEvents,
            Created      = created,
            Deleted      = deleted,
            DeletedComp  = deletedComp,
            CompDelta    = compDelta,
        });
    }

    protected override void Awake()
    {
        base.Awake();
        _tick = 0;

        _componentTypeRegistry = new TypeRegistry<IComponent>();
        _componentTypeRegistry.Register<PositionComponent>(0);
        _componentTypeRegistry.Register<RandomWalkComponent>(1);
        _componentTypeRegistry.Register<PlayerComponent>(2);

        _inputTypeRegistry.Register<SpawnEntityInput>(0);
        _inputTypeRegistry.Register<MoveInput>(1);

        _flagEventTypeRegistry.Register<EntityCreatedEvent>(0);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PositionComponent>>(1);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RandomWalkComponent>>(2);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PlayerComponent>>(4);

        _flagEventTypeRegistry.Register<PositionUpdatedEvent>(3);

        ECS = CreateSimulationECS();
    }

    // Called on a client when GameStart is received — before the game begins ticking.
    public void SetupClientECS()
    {
        ClientLocalECS = CreateSimulationECS();
        ClientServerMirrorECS = CreateMirrorECS();
        _clientDeltaManager = new ComponentDeltaManager(ClientServerMirrorECS, _componentTypeRegistry);
    }

    public void StartGame() => _gameStarted = true;

    void Update()
    {
        if (!_gameStarted) return;
        _timer += Time.deltaTime;
        float effectiveInterval = TickInterval / _tickRateScale;
        if (_timer < effectiveInterval) return;
        _timer -= effectiveInterval;
        DoTick();
    }

    void DoTick()
    {
        bool isServer = NetworkManager.instance != null && NetworkManager.instance.IsServer;
        bool isClient = NetworkManager.instance != null && NetworkManager.instance.IsClient;
        bool isPureClient = isClient && !isServer;

        if (!isPureClient)
        {
            ECS.CurrentSimulationTick = _tick;
            ECS.ExecuteSystems();
            LastTickFlagEvents = isServer
                ? ECS.FlagEvents.SerializePending(_flagEventTypeRegistry)
                : Array.Empty<byte>();
            ECS.FlagEvents.Flush();
        }

        if (isPureClient && ClientLocalECS != null)
        {
            // Reconciliation: apply all queued server deltas, then resimulate from the oldest.
            if (_pendingDeltas.Count > 0)
            {
                ulong oldestServerTick = ulong.MaxValue;

                while (_pendingDeltas.TryDequeue(out PendingDelta delta))
                {
                    _clientDeltaManager.ApplyDelta(ClientServerMirrorECS,
                        delta.Created, delta.Deleted, delta.DeletedComp, delta.CompDelta);
                    ServerFlagEvents.AddFromBytes(delta.FlagEvents, _flagEventTypeRegistry);
                    if (delta.ServerTick < oldestServerTick)
                        oldestServerTick = delta.ServerTick;
                }

                ClientLocalECS.CopyStateFrom(ClientServerMirrorECS);
                ServerFlagEvents.Flush();

                // Replay all client ticks that occurred after the oldest applied server tick.
                ulong ticksToReplay = _tick > oldestServerTick ? _tick - oldestServerTick : 0;
                for (ulong i = 0; i < ticksToReplay; i++)
                {
                    ClientLocalECS.CurrentSimulationTick = oldestServerTick + i;
                    ClientLocalECS.ExecuteSystems();
                    ClientLocalECS.FlagEvents.Flush();
                }
            }

            // Normal tick.
            ClientLocalECS.CurrentSimulationTick = _tick;
            ClientLocalECS.ExecuteSystems();
            ClientLocalECS.FlagEvents.Flush();
        }

        _tick++;
        AfterTick?.Invoke();
    }

    // Simulation ECS: component stores + systems. Used for server ECS and client local prediction.
    private ECS CreateSimulationECS()
    {
        var ecs = new ECS();
        ecs.AddComponentStore(new ComponentStore<PositionComponent>());
        ecs.AddComponentStore(new ComponentStore<RandomWalkComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerComponent>());
        ecs.RegisterSystem(SpawnEntitySystem.Instance);
        ecs.RegisterSystem(PlayerMovementSystem.Instance);
        ecs.RegisterSystem(RandomWalkSystem.Instance);
        return ecs;
    }

    // Mirror ECS: component stores only. Receives deltas; never runs systems.
    private ECS CreateMirrorECS()
    {
        var ecs = new ECS();
        ecs.AddComponentStore(new ComponentStore<PositionComponent>());
        ecs.AddComponentStore(new ComponentStore<RandomWalkComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerComponent>());
        return ecs;
    }
}
