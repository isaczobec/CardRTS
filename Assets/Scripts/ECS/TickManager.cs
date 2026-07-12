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

    public const float TickInterval = 0.05f;

    // ── Seconds ⇄ ticks conversions ─────────────────────────────────────────
    // Durations/rates should be authored as seconds constants at the call site and
    // converted here, rather than hard-coding raw tick counts throughout the codebase.
    public static int SecondsToTicks(float seconds) => Mathf.RoundToInt(seconds / TickInterval);
    public static int SecondsToTicksFloor(float seconds) => Mathf.FloorToInt(seconds / TickInterval);
    public static int SecondsToTicksCeil(float seconds) => Mathf.CeilToInt(seconds / TickInterval);
    public static float SecondsToTicksExact(float seconds) => seconds / TickInterval;

    public static float TicksToSeconds(float ticks) => ticks * TickInterval;

    public static int MillisecondsToTicks(float milliseconds) => SecondsToTicks(milliseconds / 1000f);
    public static int MillisecondsToTicksFloor(float milliseconds) => SecondsToTicksFloor(milliseconds / 1000f);
    public static int MillisecondsToTicksCeil(float milliseconds) => SecondsToTicksCeil(milliseconds / 1000f);
    public static float MillisecondsToTicksExact(float milliseconds) => SecondsToTicksExact(milliseconds / 1000f);

    public static float TicksToMilliseconds(float ticks) => TicksToSeconds(ticks) * 1000f;

    private ulong _tick;       // prediction tick — timer-driven, used by ClientLocalECS and clients
    private ulong _serverTick; // authoritative tick — lockstep-driven, used by server ECS

    public ulong Tick       => _tick;
    public ulong ServerTick => _serverTick;
    public float TimeSinceLastTick => _timer;

    private float _timer;
    private bool _gameStarted = false;
    public bool IsGameStarted => _gameStarted;

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
        _componentTypeRegistry.Register<HealthComponent>(7);
        _componentTypeRegistry.Register<StatsComponent>(8);
        _componentTypeRegistry.Register<BasicMeleeAIComponent>(9);
        _componentTypeRegistry.Register<ProjectileOwnerComponent>(10);
        _componentTypeRegistry.Register<ProjectileBaseComponent>(11);
        _componentTypeRegistry.Register<BasicRangedAIComponent>(12);
        _componentTypeRegistry.Register<SeekingProjectileComponent>(13);
        _componentTypeRegistry.Register<BuildingComponent>(14);
        _componentTypeRegistry.Register<CardComponent>(15);
        _componentTypeRegistry.Register<PlayerDeckComponent>(16);
        _componentTypeRegistry.Register<PlayerResourcesComponent>(17);
        _componentTypeRegistry.Register<RespawnableInPlaceComponent>(18);

        _inputTypeRegistry.Register<SpawnEntityInput>(0);
        _inputTypeRegistry.Register<MoveInput>(1);
        _inputTypeRegistry.Register<SpawnTroopInput>(2);
        _inputTypeRegistry.Register<MoveTroopInput>(3);
        _inputTypeRegistry.Register<SetTargetsInput>(4);
        _inputTypeRegistry.Register<CardPlayedInput>(5);

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
        _flagEventTypeRegistry.Register<TroopActivatedEvent>(17);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<HealthComponent>>(18);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<HealthComponent>>(19);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<StatsComponent>>(20);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<StatsComponent>>(21);
        _flagEventTypeRegistry.Register<TroopDiedEvent>(22);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BasicMeleeAIComponent>>(23);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BasicMeleeAIComponent>>(24);
        _flagEventTypeRegistry.Register<DamageDealtEvent>(25);
        _flagEventTypeRegistry.Register<TroopBeginAttackEvent>(26);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ProjectileOwnerComponent>>(27);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ProjectileOwnerComponent>>(28);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ProjectileBaseComponent>>(29);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ProjectileBaseComponent>>(30);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BasicRangedAIComponent>>(31);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BasicRangedAIComponent>>(32);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<SeekingProjectileComponent>>(33);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<SeekingProjectileComponent>>(34);
        _flagEventTypeRegistry.Register<ProjectileActivatedEvent>(35);
        _flagEventTypeRegistry.Register<ProjectileDeactivatedEvent>(36);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BuildingComponent>>(37);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BuildingComponent>>(38);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<CardComponent>>(39);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<CardComponent>>(40);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PlayerDeckComponent>>(41);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<PlayerDeckComponent>>(42);
        _flagEventTypeRegistry.Register<CardDrawnEvent>(43);
        _flagEventTypeRegistry.Register<CardPlayedEvent>(44);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PlayerResourcesComponent>>(45);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<PlayerResourcesComponent>>(46);
        _flagEventTypeRegistry.Register<ResourcesChangedEvent>(47);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RespawnableInPlaceComponent>>(48);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<RespawnableInPlaceComponent>>(49);
        _flagEventTypeRegistry.Register<RespawnableEntityDiedEvent>(50);
        _flagEventTypeRegistry.Register<RespawnableEntityRespawnedEvent>(51);

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
            // ClientLocalECS's delta is never serialized (unlike the authoritative ECS's,
            // drained via GetComponentsDelta et al. in NetworkManager) — discard it here
            // instead of letting it grow for the life of the session.
            ClientLocalECS.Delta.ClearPendingState();

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

        // Same reasoning as ClearPendingState in DoPredictionTick — ApplyDelta's
        // ecs.DeleteEntity calls above touch this ECS's own delta bookkeeping too, and
        // it's likewise never read.
        ClientServerMirrorECS.Delta.ClearPendingState();

        ClientLocalECS.CopyStateFrom(ClientServerMirrorECS);
        ServerFlagEvents.Flush();

        ulong ticksToReplay = _tick > newestServerTick ? _tick - newestServerTick : 0;
        for (ulong i = 0; i < ticksToReplay; i++)
        {
            ClientLocalECS.CurrentSimulationTick = newestServerTick + i;
            ClientLocalECS.ExecuteSystems();
            ClientLocalECS.FlagEvents.Flush();
            ClientLocalECS.Delta.DispatchComponentChangedEvents();
            ClientLocalECS.Delta.ClearPendingState();
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
        ecs.AddComponentStore(new ComponentStore<HealthComponent>());
        ecs.AddComponentStore(new ComponentStore<StatsComponent>());
        ecs.AddComponentStore(new ComponentStore<BasicMeleeAIComponent>());
        ecs.AddComponentStore(new ComponentStore<ProjectileOwnerComponent>());
        ecs.AddComponentStore(new ComponentStore<ProjectileBaseComponent>());
        ecs.AddComponentStore(new ComponentStore<BasicRangedAIComponent>());
        ecs.AddComponentStore(new ComponentStore<SeekingProjectileComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingComponent>());
        ecs.AddComponentStore(new ComponentStore<CardComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerDeckComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerResourcesComponent>());
        ecs.AddComponentStore(new ComponentStore<RespawnableInPlaceComponent>());
        // ecs.RegisterSystem(SpawnEntitySystem.Instance);
        ecs.RegisterSystem(SpawnTroopSystem.Instance);
        ecs.RegisterSystem(TroopActivationSystem.Instance);
        // ecs.RegisterSystem(PlayerMovementSystem.Instance);
        // ecs.RegisterSystem(RandomWalkSystem.Instance);
        ecs.RegisterSystem(new TargetingSystem());
        ecs.RegisterSystem(new BasicMeleeAISystem());
        ecs.RegisterSystem(new BasicRangedAISystem());
        ecs.RegisterSystem(new PathfindingSystem());
        ecs.RegisterSystem(new BuildingBlockingSystem());
        ecs.RegisterSystem(SeekingProjectileSystem.Instance);
        ecs.RegisterSystem(CardPlaySystem.Instance);
        ecs.RegisterSystem(new DeckSystem());
        ecs.RegisterSystem(DamageResolutionSystem.Instance);
        ecs.RegisterSystem(DeathSystem.Instance);
        ecs.RegisterSystem(RespawnSystem.Instance);
        ecs.RegisterSystem(ProjectilePoolCleanupSystem.Instance);
        ecs.RegisterSystem(ResourceGenerationSystem.Instance);
        ecs.SetupSystems();
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
        ecs.AddComponentStore(new ComponentStore<HealthComponent>());
        ecs.AddComponentStore(new ComponentStore<StatsComponent>());
        ecs.AddComponentStore(new ComponentStore<BasicMeleeAIComponent>());
        ecs.AddComponentStore(new ComponentStore<ProjectileOwnerComponent>());
        ecs.AddComponentStore(new ComponentStore<ProjectileBaseComponent>());
        ecs.AddComponentStore(new ComponentStore<BasicRangedAIComponent>());
        ecs.AddComponentStore(new ComponentStore<SeekingProjectileComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingComponent>());
        ecs.AddComponentStore(new ComponentStore<CardComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerDeckComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerResourcesComponent>());
        ecs.AddComponentStore(new ComponentStore<RespawnableInPlaceComponent>());

        return ecs;
    }
}
