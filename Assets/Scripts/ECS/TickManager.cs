using System;
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
    // Receives deserialized server flag events on clients. Subscribe here to react to server-side events.
    public FlagEventManager ServerFlagEvents { get; } = new FlagEventManager();
    // Serialized flag events from the last server tick, bundled into the outgoing SimulationDelta.
    public byte[] LastTickFlagEvents { get; private set; } = Array.Empty<byte>();

    private TypeRegistry<IComponent> _componentTypeRegistry;
    public TypeRegistry<IComponent> ComponentTypeRegistry => _componentTypeRegistry;

    private readonly TypeRegistry<FlagEvent> _flagEventTypeRegistry = new();
    public TypeRegistry<FlagEvent> FlagEventTypeRegistry => _flagEventTypeRegistry;

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

    protected override void Awake()
    {
        base.Awake();
        _tick = 0;

        _componentTypeRegistry = new TypeRegistry<IComponent>();
        _componentTypeRegistry.Register<PositionComponent>(0);
        _componentTypeRegistry.Register<RandomWalkComponent>(1);

        _flagEventTypeRegistry.Register<EntityCreatedEvent>(0);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PositionComponent>>(1);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RandomWalkComponent>>(2);

        ECS = CreateSimulationECS();
    }

    // Called on a client when GameStart is received — before the game begins ticking.
    public void SetupClientECS()
    {
        ClientLocalECS = CreateSimulationECS();
        ClientServerMirrorECS = CreateMirrorECS();
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
            ECS.ExecuteSystems();
            LastTickFlagEvents = isServer
                ? ECS.FlagEvents.SerializePending(_flagEventTypeRegistry)
                : Array.Empty<byte>();
            ECS.FlagEvents.Flush();
        }

        if (isPureClient && ClientLocalECS != null)
        {
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
        ecs.RegisterSystem(RandomWalkSystem.Instance);
        return ecs;
    }

    // Mirror ECS: component stores only. Receives deltas; never runs systems.
    private ECS CreateMirrorECS()
    {
        var ecs = new ECS();
        ecs.AddComponentStore(new ComponentStore<PositionComponent>());
        ecs.AddComponentStore(new ComponentStore<RandomWalkComponent>());
        return ecs;
    }
}
