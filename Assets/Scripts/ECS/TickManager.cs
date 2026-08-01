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

    // Shared safety cap for both tick "catch up" loops below (prediction tick in Update,
    // server tick in TryRunServerTick) — bounds how many ticks either loop will run in a
    // single rendered frame so a big backlog (e.g. after a stall/hitch) drains over several
    // frames instead of spending unbounded time in one Update() call.
    private const int MaxTickCatchupPerFrame = 20;

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

    // Throttles the local prediction tick rate to track referenceTick (an authoritative
    // server tick), the same way for any predictor: a pure client calls this with the
    // serverTick it just received over the network (see NetworkManager.OnSimulationDelta);
    // the host calls it with its own _serverTick directly each frame (see Update below) —
    // no round trip needed, since the host already knows its authoritative tick locally.
    // Without this, prediction just free-runs at the local frame rate and can drift
    // arbitrarily far ahead of whatever the authoritative tick actually is (e.g. held back
    // by a slower remote client's lockstep input), which is invisible for predicted-only
    // visuals but shows up as a growing lag for anything gated on server confirmation
    // (DamageDealtEvent-driven damage numbers/health bars, both ServerFlagEvents-only).
    public void AdjustTickRateToServerTick(ulong referenceTick)
    {
        long tickError = (long)referenceTick - (long)_tick;
        float scale = 1f + Mathf.Clamp(tickError * 0.1f, -0.5f, 0.5f);
        SetTickRateScale(scale);
    }

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
        _componentTypeRegistry.Register<OnDeathResourceDropComponent>(19);
        _componentTypeRegistry.Register<ActivatableComponent>(20);
        _componentTypeRegistry.Register<DamageAuraComponent>(21);
        _componentTypeRegistry.Register<LifetimeComponent>(22);
        _componentTypeRegistry.Register<SkillshotProjectileComponent>(23);
        _componentTypeRegistry.Register<AbilityComponent>(24);
        _componentTypeRegistry.Register<ModifierComponent>(25);
        _componentTypeRegistry.Register<StatModifierComponent>(26);
        _componentTypeRegistry.Register<RenderableModifierComponent>(27);
        _componentTypeRegistry.Register<AIModeComponent>(28);
        _componentTypeRegistry.Register<TeleportingModifierComponent>(29);
        _componentTypeRegistry.Register<BlinkComponent>(30);
        _componentTypeRegistry.Register<RespawnCooldownRampComponent>(31);
        _componentTypeRegistry.Register<ResourceProductionOnDeathComponent>(32);
        _componentTypeRegistry.Register<UpgradeComponent>(33);
        _componentTypeRegistry.Register<ShopPurchaseHistoryComponent>(34);
        _componentTypeRegistry.Register<PeriodicDamageReductionComponent>(35);
        _componentTypeRegistry.Register<ActionWindupComponent>(36);
        _componentTypeRegistry.Register<FireProjectileOnExpireComponent>(37);
        _componentTypeRegistry.Register<ProjectileOnHitComponent>(38);
        _componentTypeRegistry.Register<StunnedComponent>(39);
        _componentTypeRegistry.Register<ScheduledCallComponent>(40);
        _componentTypeRegistry.Register<DamageOverTimeComponent>(41);
        _componentTypeRegistry.Register<StackingBurnDebuffComponent>(42);
        _componentTypeRegistry.Register<BuildingDamageBonusComponent>(43);
        _componentTypeRegistry.Register<OnKillScheduleComponent>(44);
        _componentTypeRegistry.Register<ShadowCloakComponent>(45);
        _componentTypeRegistry.Register<OnHitScheduleComponent>(46);
        _componentTypeRegistry.Register<BarrierComponent>(47);
        _componentTypeRegistry.Register<RootedComponent>(48);
        _componentTypeRegistry.Register<TurretAIComponent>(49);
        _componentTypeRegistry.Register<BallisticProjectileComponent>(50);
        _componentTypeRegistry.Register<ResourceGeneratorComponent>(51);
        _componentTypeRegistry.Register<PeriodicAreaEffectComponent>(52);
        _componentTypeRegistry.Register<HealModifierComponent>(53);
        _componentTypeRegistry.Register<HealSourceComponent>(54);
        _componentTypeRegistry.Register<GiantsbaneComponent>(55);
        _componentTypeRegistry.Register<FocusFireComponent>(56);
        _componentTypeRegistry.Register<LifestealComponent>(57);
        _componentTypeRegistry.Register<DeflectionComponent>(58);
        _componentTypeRegistry.Register<BruiserComponent>(59);
        _componentTypeRegistry.Register<ShadowAngelDamageShareComponent>(60);
        _componentTypeRegistry.Register<ShadowAngelComponent>(61);
        _componentTypeRegistry.Register<ResourceValueComponent>(62);
        _componentTypeRegistry.Register<PlayerTotalResourceValueComponent>(63);
        _componentTypeRegistry.Register<SilenceComponent>(64);
        _componentTypeRegistry.Register<RemoveModifierOnDamageComponent>(65);
        _componentTypeRegistry.Register<BuildingRefundAuraComponent>(66);
        _componentTypeRegistry.Register<StatAuraSourceComponent>(67);
        _componentTypeRegistry.Register<ResourceDropBoostComponent>(68);
        _componentTypeRegistry.Register<TornadoProjectileComponent>(69);

        _inputTypeRegistry.Register<SpawnEntityInput>(0);
        _inputTypeRegistry.Register<MoveInput>(1);
        _inputTypeRegistry.Register<SpawnTroopInput>(2);
        _inputTypeRegistry.Register<MoveTroopInput>(3);
        _inputTypeRegistry.Register<SetTargetsInput>(4);
        _inputTypeRegistry.Register<SpawnAtPointInput>(5);
        _inputTypeRegistry.Register<AbilityUsedInput>(6);
        _inputTypeRegistry.Register<AbilityUsedAtLocationInput>(7);
        _inputTypeRegistry.Register<AbilityUsedOnEntityInput>(8);
        _inputTypeRegistry.Register<SpawnAtEntityInput>(9);
        _inputTypeRegistry.Register<BuyCardInput>(10);
        _inputTypeRegistry.Register<SetAIModeInput>(11);
        _inputTypeRegistry.Register<MultiPointInput>(12);
        _inputTypeRegistry.Register<BuyUpgradeInput>(13);

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
        _flagEventTypeRegistry.Register<EntityActivatedEvent>(17);
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
        _flagEventTypeRegistry.Register<ComponentAddedEvent<OnDeathResourceDropComponent>>(52);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<OnDeathResourceDropComponent>>(53);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ActivatableComponent>>(54);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ActivatableComponent>>(55);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<DamageAuraComponent>>(56);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<DamageAuraComponent>>(57);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<LifetimeComponent>>(58);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<LifetimeComponent>>(59);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<SkillshotProjectileComponent>>(60);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<SkillshotProjectileComponent>>(61);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<AbilityComponent>>(62);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<AbilityComponent>>(63);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ModifierComponent>>(64);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ModifierComponent>>(65);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<StatModifierComponent>>(66);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<StatModifierComponent>>(67);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RenderableModifierComponent>>(68);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<RenderableModifierComponent>>(69);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<AIModeComponent>>(70);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<AIModeComponent>>(71);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<TeleportingModifierComponent>>(72);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<TeleportingModifierComponent>>(73);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BlinkComponent>>(74);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BlinkComponent>>(75);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RespawnCooldownRampComponent>>(76);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<RespawnCooldownRampComponent>>(77);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ResourceProductionOnDeathComponent>>(78);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ResourceProductionOnDeathComponent>>(79);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<UpgradeComponent>>(80);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<UpgradeComponent>>(81);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ShopPurchaseHistoryComponent>>(82);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ShopPurchaseHistoryComponent>>(83);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PeriodicDamageReductionComponent>>(84);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<PeriodicDamageReductionComponent>>(85);
        _flagEventTypeRegistry.Register<PeriodicDamageReductionProcEvent>(86);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ActionWindupComponent>>(87);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ActionWindupComponent>>(88);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<FireProjectileOnExpireComponent>>(89);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<FireProjectileOnExpireComponent>>(90);
        _flagEventTypeRegistry.Register<AbilityPerformedEvent>(91);
        _flagEventTypeRegistry.Register<AttackWindupBeganEvent>(92);
        _flagEventTypeRegistry.Register<AttackWindupFinishedEvent>(93);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ProjectileOnHitComponent>>(94);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ProjectileOnHitComponent>>(95);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<StunnedComponent>>(96);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<StunnedComponent>>(97);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ScheduledCallComponent>>(98);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ScheduledCallComponent>>(99);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<DamageOverTimeComponent>>(100);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<DamageOverTimeComponent>>(101);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<StackingBurnDebuffComponent>>(102);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<StackingBurnDebuffComponent>>(103);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BuildingDamageBonusComponent>>(104);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BuildingDamageBonusComponent>>(105);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<OnKillScheduleComponent>>(106);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<OnKillScheduleComponent>>(107);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ShadowCloakComponent>>(108);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ShadowCloakComponent>>(109);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<OnHitScheduleComponent>>(110);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<OnHitScheduleComponent>>(111);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BarrierComponent>>(112);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BarrierComponent>>(113);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RootedComponent>>(114);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<RootedComponent>>(115);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<TurretAIComponent>>(116);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<TurretAIComponent>>(117);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BallisticProjectileComponent>>(118);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BallisticProjectileComponent>>(119);
        _flagEventTypeRegistry.Register<LinkedProjectileFiredEvent>(120);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ResourceGeneratorComponent>>(121);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ResourceGeneratorComponent>>(122);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PeriodicAreaEffectComponent>>(123);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<PeriodicAreaEffectComponent>>(124);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<HealModifierComponent>>(125);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<HealModifierComponent>>(126);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<HealSourceComponent>>(127);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<HealSourceComponent>>(128);
        _flagEventTypeRegistry.Register<HealDealtEvent>(129);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<GiantsbaneComponent>>(130);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<GiantsbaneComponent>>(131);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<FocusFireComponent>>(132);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<FocusFireComponent>>(133);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<LifestealComponent>>(134);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<LifestealComponent>>(135);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<DeflectionComponent>>(136);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<DeflectionComponent>>(137);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BruiserComponent>>(138);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BruiserComponent>>(139);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ShadowAngelDamageShareComponent>>(140);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ShadowAngelDamageShareComponent>>(141);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ShadowAngelComponent>>(142);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ShadowAngelComponent>>(143);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ResourceValueComponent>>(144);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ResourceValueComponent>>(145);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<PlayerTotalResourceValueComponent>>(146);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<PlayerTotalResourceValueComponent>>(147);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<SilenceComponent>>(148);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<SilenceComponent>>(149);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<RemoveModifierOnDamageComponent>>(150);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<RemoveModifierOnDamageComponent>>(151);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<BuildingRefundAuraComponent>>(152);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<BuildingRefundAuraComponent>>(153);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<StatAuraSourceComponent>>(154);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<StatAuraSourceComponent>>(155);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<ResourceDropBoostComponent>>(156);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<ResourceDropBoostComponent>>(157);
        _flagEventTypeRegistry.Register<ComponentAddedEvent<TornadoProjectileComponent>>(158);
        _flagEventTypeRegistry.Register<ComponentRemovedEvent<TornadoProjectileComponent>>(159);

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
        // On a host, this is reachable from two places (NetworkManager.NotifyClientReady's
        // server-side path and OnGameReady's client-side path, since a host runs both) —
        // guard so SetupRendering/FireGameStarting only ever run once regardless of how
        // many of those fire.
        if (_gameStarted) return;

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
        //
        // Must catch up (run more than once per frame) rather than cap at a single tick,
        // mirroring TryRunServerTick's own maxCatchup loop below — otherwise, whenever the
        // frame rate drops below the tick rate, _timer accumulates backlog faster than a
        // single tick can drain it, permanently capping this machine's effective tick (and
        // therefore SendClientTickInput/NotifyHostTickReady) rate at its frame rate instead
        // of the intended tick rate. Since TryRunServerTick blocks on every connected
        // client's input for a given server tick, one client's low frame rate throttling its
        // own input-submission rate this way stalls the whole lockstep group behind it.
        _timer += Time.deltaTime;
        float effectiveInterval = TickInterval / _tickRateScale;
        int predictionTicksRan = 0;
        while (predictionTicksRan++ < MaxTickCatchupPerFrame && _timer >= effectiveInterval)
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
        {
            TryRunServerTick();

            // Host: throttle local prediction to track its own authoritative _serverTick,
            // the same way AdjustTickRateToServerTick does for a pure client using the
            // network-received serverTick (see NetworkManager.OnSimulationDelta). The host
            // never receives its own SimulationDelta (that path early-returns via IsServer),
            // so without this call _tickRateScale stays pinned at 1 and prediction can race
            // arbitrarily far ahead of the lockstep-gated server tick when a remote client
            // is running behind.
            if (ClientLocalECS != null)
                AdjustTickRateToServerTick(_serverTick);
        }
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
        int ran = 0;
        while (ran++ < MaxTickCatchupPerFrame && NetworkManager.instance.IsAllClientsReadyForTick(_serverTick))
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
        ecs.AddComponentStore(new ComponentStore<SkillshotProjectileComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingComponent>());
        ecs.AddComponentStore(new ComponentStore<CardComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerDeckComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerResourcesComponent>());
        ecs.AddComponentStore(new ComponentStore<RespawnableInPlaceComponent>());
        ecs.AddComponentStore(new ComponentStore<OnDeathResourceDropComponent>());
        ecs.AddComponentStore(new ComponentStore<ActivatableComponent>());
        ecs.AddComponentStore(new ComponentStore<DamageAuraComponent>());
        ecs.AddComponentStore(new ComponentStore<LifetimeComponent>());
        ecs.AddComponentStore(new ComponentStore<AbilityComponent>());
        ecs.AddComponentStore(new ComponentStore<ModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<StatModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<RenderableModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<AIModeComponent>());
        ecs.AddComponentStore(new ComponentStore<TeleportingModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<BlinkComponent>());
        ecs.AddComponentStore(new ComponentStore<RespawnCooldownRampComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceProductionOnDeathComponent>());
        ecs.AddComponentStore(new ComponentStore<UpgradeComponent>());
        ecs.AddComponentStore(new ComponentStore<ShopPurchaseHistoryComponent>());
        ecs.AddComponentStore(new ComponentStore<PeriodicDamageReductionComponent>());
        ecs.AddComponentStore(new ComponentStore<ActionWindupComponent>());
        ecs.AddComponentStore(new ComponentStore<FireProjectileOnExpireComponent>());
        ecs.AddComponentStore(new ComponentStore<ProjectileOnHitComponent>());
        ecs.AddComponentStore(new ComponentStore<StunnedComponent>());
        ecs.AddComponentStore(new ComponentStore<ShadowCloakComponent>());
        ecs.AddComponentStore(new ComponentStore<ScheduledCallComponent>());
        ecs.AddComponentStore(new ComponentStore<DamageOverTimeComponent>());
        ecs.AddComponentStore(new ComponentStore<StackingBurnDebuffComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingDamageBonusComponent>());
        ecs.AddComponentStore(new ComponentStore<OnKillScheduleComponent>());
        ecs.AddComponentStore(new ComponentStore<OnHitScheduleComponent>());
        ecs.AddComponentStore(new ComponentStore<BarrierComponent>());
        ecs.AddComponentStore(new ComponentStore<RootedComponent>());
        ecs.AddComponentStore(new ComponentStore<TurretAIComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceGeneratorComponent>());
        ecs.AddComponentStore(new ComponentStore<BallisticProjectileComponent>());
        ecs.AddComponentStore(new ComponentStore<PeriodicAreaEffectComponent>());
        ecs.AddComponentStore(new ComponentStore<HealModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<HealSourceComponent>());
        ecs.AddComponentStore(new ComponentStore<GiantsbaneComponent>());
        ecs.AddComponentStore(new ComponentStore<FocusFireComponent>());
        ecs.AddComponentStore(new ComponentStore<LifestealComponent>());
        ecs.AddComponentStore(new ComponentStore<DeflectionComponent>());
        ecs.AddComponentStore(new ComponentStore<BruiserComponent>());
        ecs.AddComponentStore(new ComponentStore<ShadowAngelDamageShareComponent>());
        ecs.AddComponentStore(new ComponentStore<ShadowAngelComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceValueComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerTotalResourceValueComponent>());
        ecs.AddComponentStore(new ComponentStore<SilenceComponent>());
        ecs.AddComponentStore(new ComponentStore<RemoveModifierOnDamageComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingRefundAuraComponent>());
        ecs.AddComponentStore(new ComponentStore<StatAuraSourceComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceDropBoostComponent>());
        ecs.AddComponentStore(new ComponentStore<TornadoProjectileComponent>());
        // ecs.RegisterSystem(SpawnEntitySystem.Instance);
        ecs.RegisterSystem(SpawnTroopSystem.Instance);
        ecs.RegisterSystem(ActivationSystem.Instance);
        // Early — well before DamageResolutionSystem's Flush<DamageRequest> (much later this
        // same tick) can invoke ShadowAngelDamageShareSystem's own subscriber — see
        // ShadowAngelTickResetSystem's own doc comment for why it can't just live inside that
        // system's Execute instead.
        ecs.RegisterSystem(ShadowAngelTickResetSystem.Instance);
        ecs.RegisterSystem(LifetimeSystem.Instance);
        // Must run before ModifierSystem — it checks TicksRemaining <= 1 to fire on a
        // modifier's very last active tick, before ModifierSystem decrements it to 0 and
        // deletes the entity (see FireProjectileOnExpireSystem's own doc comment).
        ecs.RegisterSystem(FireProjectileOnExpireSystem.Instance);
        ecs.RegisterSystem(ModifierSystem.Instance);
        ecs.RegisterSystem(StatModifierSystem.Instance);
        // Just needs to run before ArmorMitigationSystem/DamageResolutionSystem so the
        // DamageRequests it creates get processed the same tick — no ordering dependency on
        // anything else here (see DamageOverTimeSystem's own doc comment).
        ecs.RegisterSystem(DamageOverTimeSystem.Instance);
        // Same ordering reasoning as DamageOverTimeSystem above, just for HealRequest/
        // HealResolutionSystem instead.
        ecs.RegisterSystem(HealModifierSystem.Instance);
        ecs.RegisterSystem(ActionWindupSystem.Instance);
        ecs.RegisterSystem(StunnedSystem.Instance);
        ecs.RegisterSystem(ShadowCloakSystem.Instance);
        // Must run before DisplacementSystem — see TornadoProjectileSystem's own doc comment
        // on why a pull it issues needs to be applied by DisplacementSystem the SAME tick.
        ecs.RegisterSystem(TornadoProjectileSystem.Instance);
        ecs.RegisterSystem(DisplacementSystem.Instance);
        ecs.RegisterSystem(RootedSystem.Instance);
        ecs.RegisterSystem(SilenceSystem.Instance);
        ecs.RegisterSystem(new ScheduledCallSystem());
        ecs.RegisterSystem(new TeleportingModifierSystem());
        ecs.RegisterSystem(new BlinkSystem());
        // ecs.RegisterSystem(PlayerMovementSystem.Instance);
        // ecs.RegisterSystem(RandomWalkSystem.Instance);
        ecs.RegisterSystem(new TargetingSystem());
        ecs.RegisterSystem(SetAIModeSystem.Instance);
        ecs.RegisterSystem(new BasicMeleeAISystem());
        ecs.RegisterSystem(new BasicRangedAISystem());
        ecs.RegisterSystem(new TurretAISystem());
        ecs.RegisterSystem(new PathfindingSystem());
        ecs.RegisterSystem(new BuildingBlockingSystem());
        ecs.RegisterSystem(SeekingProjectileSystem.Instance);
        ecs.RegisterSystem(SkillshotProjectileSystem.Instance);
        ecs.RegisterSystem(DamageAuraSystem.Instance);
        ecs.RegisterSystem(PeriodicAreaEffectSystem.Instance);
        ecs.RegisterSystem(SpawnAtPointCardPlaySystem.Instance);
        ecs.RegisterSystem(TargetEntityCardPlaySystem.Instance);
        ecs.RegisterSystem(MultiPointCardPlaySystem.Instance);
        ecs.RegisterSystem(BuyCardSystem.Instance);
        ecs.RegisterSystem(BuyUpgradeSystem.Instance);
        ecs.RegisterSystem(AbilitySystem.Instance);
        ecs.RegisterSystem(new DeckSystem());
        ecs.RegisterSystem(ProjectileOnHitSystem.Instance);
        ecs.RegisterSystem(ProjectileHitResolutionSystem.Instance);
        // Must be registered (thus Setup/Subscribe<DamageRequest>) before ArmorMitigationSystem
        // — Subscribe callbacks run in registration order (see RequestManager's own doc
        // comment), so this boosts the raw damage first, before ArmorMitigationSystem
        // mitigates the (now-boosted) amount — see BuildingDamageBonusSystem's own comment.
        ecs.RegisterSystem(BuildingDamageBonusSystem.Instance);
        ecs.RegisterSystem(ArmorMitigationSystem.Instance);
        // Right after ArmorMitigationSystem so it acts on the already-armor-mitigated Amount,
        // same ordering reasoning as PeriodicDamageReductionSystem right below it — see
        // DeflectionSystem's own doc comment.
        ecs.RegisterSystem(DeflectionSystem.Instance);
        ecs.RegisterSystem(PeriodicDamageReductionSystem.Instance);
        ecs.RegisterSystem(BarrierSystem.Instance);
        // So it defers a fraction of the final, fully-mitigated Amount — see BruiserSystem's
        // own doc comment.
        ecs.RegisterSystem(BruiserSystem.Instance);
        // Registered last among every DamageRequest subscriber (Subscribe callbacks fire in
        // registration order — see RequestManager's own doc comment) so it redirects whatever
        // Amount survived every other mitigation/deferral above, right before
        // DamageResolutionSystem's Flush actually applies whatever's left to the Shadow
        // Angel's own HealthComponent — see ShadowAngelDamageShareSystem's own doc comment.
        ecs.RegisterSystem(ShadowAngelDamageShareSystem.Instance);
        ecs.RegisterSystem(DamageResolutionSystem.Instance);
        ecs.RegisterSystem(HealResolutionSystem.Instance);
        ecs.RegisterSystem(HitboxImmunitySystem.Instance);
        ecs.RegisterSystem(AbilityCooldownSystem.Instance);
        ecs.RegisterSystem(AbilityChargeSystem.Instance);
        ecs.RegisterSystem(DeathSystem.Instance);
        ecs.RegisterSystem(RespawnSystem.Instance);
        ecs.RegisterSystem(RespawnCooldownRampSystem.Instance);
        ecs.RegisterSystem(OnDeathResourceDropSystem.Instance);
        ecs.RegisterSystem(ResourceProductionOnDeathSystem.Instance);
        ecs.RegisterSystem(OnKillScheduleSystem.Instance);
        ecs.RegisterSystem(OnHitScheduleSystem.Instance);
        ecs.RegisterSystem(GiantsbaneSystem.Instance);
        ecs.RegisterSystem(FocusFireSystem.Instance);
        ecs.RegisterSystem(LifestealSystem.Instance);
        ecs.RegisterSystem(RemoveModifierOnDamageSystem.Instance);
        ecs.RegisterSystem(ProjectilePoolCleanupSystem.Instance);
        // Must run BEFORE ResourceGenerationSystem — see ResourceGeneratorSystem's own doc
        // comment: that system only enqueues ResourcesAdded requests, and
        // ResourceGenerationSystem is what actually flushes every pending one this tick.
        // ResourceCollectorTrickleSystem is the same shape (CreateRequest only queues,
        // doesn't flush — see RequestManager) so it needs the same ordering.
        ecs.RegisterSystem(ResourceGeneratorSystem.Instance);
        ecs.RegisterSystem(ResourceCollectorTrickleSystem.Instance);
        ecs.RegisterSystem(ResourceGenerationSystem.Instance);
        // Subscribe-only (mutates ResourcesAdded.Multiplier before Execute) — no ordering
        // dependency on anything else here, since it's the only subscriber that touches
        // Multiplier today.
        ecs.RegisterSystem(ResourceDropBoostSystem.Instance);
        // Subscribe-only (mutates ResourcesAdded.Multiplier before Execute), same shape as
        // ResourceDropBoostSystem right above — order between the two doesn't matter, since
        // both just multiply the same float.
        ecs.RegisterSystem(ComebackResourceBoostSystem.Instance);
        // Pure read-and-report step over whatever ResourceValueComponents exist at the end
        // of the tick — no ordering dependency on anything above, so it's registered last.
        ecs.RegisterSystem(ResourceValueTotalSystem.Instance);
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
        ecs.AddComponentStore(new ComponentStore<SkillshotProjectileComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingComponent>());
        ecs.AddComponentStore(new ComponentStore<CardComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerDeckComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerResourcesComponent>());
        ecs.AddComponentStore(new ComponentStore<RespawnableInPlaceComponent>());
        ecs.AddComponentStore(new ComponentStore<OnDeathResourceDropComponent>());
        ecs.AddComponentStore(new ComponentStore<ActivatableComponent>());
        ecs.AddComponentStore(new ComponentStore<DamageAuraComponent>());
        ecs.AddComponentStore(new ComponentStore<LifetimeComponent>());
        ecs.AddComponentStore(new ComponentStore<AbilityComponent>());
        ecs.AddComponentStore(new ComponentStore<ModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<StatModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<RenderableModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<AIModeComponent>());
        ecs.AddComponentStore(new ComponentStore<TeleportingModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<BlinkComponent>());
        ecs.AddComponentStore(new ComponentStore<RespawnCooldownRampComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceProductionOnDeathComponent>());
        ecs.AddComponentStore(new ComponentStore<UpgradeComponent>());
        ecs.AddComponentStore(new ComponentStore<ShopPurchaseHistoryComponent>());
        ecs.AddComponentStore(new ComponentStore<PeriodicDamageReductionComponent>());
        ecs.AddComponentStore(new ComponentStore<ActionWindupComponent>());
        ecs.AddComponentStore(new ComponentStore<FireProjectileOnExpireComponent>());
        ecs.AddComponentStore(new ComponentStore<ProjectileOnHitComponent>());
        ecs.AddComponentStore(new ComponentStore<StunnedComponent>());
        ecs.AddComponentStore(new ComponentStore<ShadowCloakComponent>());
        ecs.AddComponentStore(new ComponentStore<ScheduledCallComponent>());
        ecs.AddComponentStore(new ComponentStore<DamageOverTimeComponent>());
        ecs.AddComponentStore(new ComponentStore<StackingBurnDebuffComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingDamageBonusComponent>());
        ecs.AddComponentStore(new ComponentStore<OnKillScheduleComponent>());
        ecs.AddComponentStore(new ComponentStore<OnHitScheduleComponent>());
        ecs.AddComponentStore(new ComponentStore<BarrierComponent>());
        ecs.AddComponentStore(new ComponentStore<RootedComponent>());
        ecs.AddComponentStore(new ComponentStore<TurretAIComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceGeneratorComponent>());
        ecs.AddComponentStore(new ComponentStore<BallisticProjectileComponent>());
        ecs.AddComponentStore(new ComponentStore<PeriodicAreaEffectComponent>());
        ecs.AddComponentStore(new ComponentStore<HealModifierComponent>());
        ecs.AddComponentStore(new ComponentStore<HealSourceComponent>());
        ecs.AddComponentStore(new ComponentStore<GiantsbaneComponent>());
        ecs.AddComponentStore(new ComponentStore<FocusFireComponent>());
        ecs.AddComponentStore(new ComponentStore<LifestealComponent>());
        ecs.AddComponentStore(new ComponentStore<DeflectionComponent>());
        ecs.AddComponentStore(new ComponentStore<BruiserComponent>());
        ecs.AddComponentStore(new ComponentStore<ShadowAngelDamageShareComponent>());
        ecs.AddComponentStore(new ComponentStore<ShadowAngelComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceValueComponent>());
        ecs.AddComponentStore(new ComponentStore<PlayerTotalResourceValueComponent>());
        ecs.AddComponentStore(new ComponentStore<SilenceComponent>());
        ecs.AddComponentStore(new ComponentStore<RemoveModifierOnDamageComponent>());
        ecs.AddComponentStore(new ComponentStore<BuildingRefundAuraComponent>());
        ecs.AddComponentStore(new ComponentStore<StatAuraSourceComponent>());
        ecs.AddComponentStore(new ComponentStore<ResourceDropBoostComponent>());
        ecs.AddComponentStore(new ComponentStore<TornadoProjectileComponent>());

        return ecs;
    }
}
