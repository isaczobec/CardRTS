using System.Collections.Generic;
using UnityEngine;

// Drives a manual "recall to deck" channel — the player-triggered counterpart to
// SpawnedByCardComponent/CardReturnSystem's automatic "hold the card until its spawn dies"
// behavior. See RecallInput/RecallInputManager for how a hold-G-near-a-troop gesture becomes
// one of these; see RecallingComponent for what's tracked while a channel is in progress.
//
// Setup vetoes CanMoveRequest/CanMoveOnOwnAccountRequest/CanPerformRequest for any recalling
// entity — mirrors StunnedSystem/RootedSystem/SilenceSystem's own shape exactly, just for all
// three at once (a recalling troop can't walk, attack, or cast) — and cancels the recall the
// instant the entity takes any damage (SubscribeExecuted<DamageRequest>, same hook
// RemoveModifierOnDamageSystem uses). Cancelling on a fresh player order instead happens at
// each order's own point of application — see CancelRecall's own doc comment for why it's a
// public entry point rather than another Subscribe here.
//
// Execute reads RecallInput to begin new channels, then ticks every in-progress
// RecallingComponent down by one tick; a channel that reaches 0 refunds a ratio of the
// entity's own ResourceValueComponent to its owner and deletes it, exactly like a normal
// death would remove it (see SpawnedByCardComponent's own doc comment on why that's enough
// for CardReturnSystem to eventually recycle its card).
public static class RecallSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    public const float RecallDurationSeconds = 8f;

    // Soulstones refund at 1x (explicit design ask); every other resource at 0.5x.
    private const float SoulstoneRefundRatio = 1f;
    private const float DefaultRefundRatio = 0.5f;

    // Scratch buffers, reused across calls rather than allocated per call/tick — same
    // "static shared scratch list" shape LifetimeSystem/ModifierSystem/DeathSystem already
    // use for their own per-tick expired/dead lists.
    private static readonly List<ulong> _groupScratch = new List<ulong>();
    private static readonly List<ulong> _completed = new List<ulong>();

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<CanMoveRequest>((req, innerEcs) =>
        {
            if (IsRecalling(innerEcs, req.EntityId))
                req.CanMove = false;
        });

        ecs.Requests.Subscribe<CanMoveOnOwnAccountRequest>((req, innerEcs) =>
        {
            if (IsRecalling(innerEcs, req.EntityId))
                req.CanMoveOnOwnAccount = false;
        });

        ecs.Requests.Subscribe<CanPerformRequest>((req, innerEcs) =>
        {
            if (IsRecalling(innerEcs, req.EntityId))
                req.CanPerform = false;
        });

        ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);
    }

    private static bool IsRecalling(ECS ecs, ulong entityId)
    {
        ComponentStore<RecallingComponent> store = ecs.GetComponentStore<RecallingComponent>();
        return store != null && store.HasComponent(entityId);
    }

    // Damage fully absorbed/mitigated down to 0 doesn't count as "taking damage" for this
    // purpose — mirrors RemoveModifierOnDamageSystem's own identical guard.
    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        if (request.Amount <= 0) return;
        CancelRecall(ecs, request.EntityId);
    }

    // Cancels entityId's in-progress recall, if any — and, since "pair" troops from a
    // multi-spawn card recall together (see SpawnedByCardComponent), every other
    // still-recalling entity spawned by the same card instance right along with it. Called
    // both from OnDamageExecuted above and from PathfindingSystem/TargetingSystem/
    // AbilitySystem the instant a fresh move/attack-target/ability order is actually applied
    // for a recalling entity — issuing any of those is always allowed (the order goes on to
    // take effect the same tick, since CanMoveOnOwnAccountRequest/CanPerformRequest's own
    // veto above no longer applies once this runs), it just immediately ends the recall
    // rather than being silently swallowed by it.
    public static void CancelRecall(ECS ecs, ulong entityId)
    {
        ComponentStore<RecallingComponent> store = ecs.GetComponentStore<RecallingComponent>();
        if (store == null || !store.HasComponent(entityId)) return;

        ulong cardEntityId = SpawnedByCardHelper.ResolveCardEntityId(ecs, entityId);
        if (cardEntityId == 0)
        {
            RemoveIfPresent(ecs, store, entityId);
            return;
        }

        SpawnedByCardHelper.CollectAliveSpawns(ecs, cardEntityId, _groupScratch);
        foreach (ulong memberId in _groupScratch)
            RemoveIfPresent(ecs, store, memberId);
    }

    private static void RemoveIfPresent(ECS ecs, ComponentStore<RecallingComponent> store, ulong entityId)
    {
        if (store.HasComponent(entityId))
            ecs.RemoveComponent<RecallingComponent>(entityId);
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents)
    {
        ComponentStore<RecallingComponent> recallStore = ecs.GetComponentStore<RecallingComponent>();
        if (recallStore == null) return;

        List<RecallInput> inputs = ecs.GetInputsForTick<RecallInput>();
        if (inputs != null)
            foreach (RecallInput input in inputs)
                TryBeginRecall(ecs, input, recallStore);

        Tick(ecs, recallStore);
    }

    // Validates ownership/activation exactly like PathfindingSystem/TargetingSystem do for
    // their own player-issued commands, then starts the channel for the requested entity and
    // every "pair" troop spawned by the same card instance (all starting — and so finishing —
    // together). Already-recalling entities are left alone (BeginRecall is idempotent), so a
    // client re-sending the input has no effect beyond the first.
    private static void TryBeginRecall(ECS ecs, RecallInput input, ComponentStore<RecallingComponent> recallStore)
    {
        ComponentStore<TroopComponent> troopStore = ecs.GetComponentStore<TroopComponent>();
        if (troopStore == null || !troopStore.HasComponent(input.EntityId)) return;

        TroopComponent troop = troopStore.GetComponent(input.EntityId);
        if (troop.OwnerPlayerId != input.ClientId) return;
        if (!ActivationQuery.IsActivated(ecs, input.EntityId)) return;

        BeginRecall(ecs, input.EntityId, recallStore);
    }

    private static void BeginRecall(ECS ecs, ulong entityId, ComponentStore<RecallingComponent> recallStore)
    {
        int ticks = TickManager.SecondsToTicks(RecallDurationSeconds);
        ulong cardEntityId = SpawnedByCardHelper.ResolveCardEntityId(ecs, entityId);

        if (cardEntityId == 0)
        {
            AddIfAbsent(ecs, recallStore, entityId, ticks);
            return;
        }

        SpawnedByCardHelper.CollectAliveSpawns(ecs, cardEntityId, _groupScratch);
        foreach (ulong memberId in _groupScratch)
            AddIfAbsent(ecs, recallStore, memberId, ticks);
    }

    private static void AddIfAbsent(ECS ecs, ComponentStore<RecallingComponent> recallStore, ulong entityId, int ticks)
    {
        if (recallStore.HasComponent(entityId)) return;
        ecs.AddComponent(entityId, new RecallingComponent { TicksRemaining = ticks, InitialTicksRemaining = ticks });
    }

    private static void Tick(ECS ecs, ComponentStore<RecallingComponent> recallStore)
    {
        _completed.Clear();
        recallStore.ForEach((ulong id) =>
        {
            ref RecallingComponent recall = ref recallStore.GetComponent(id);

            // Already reached 0 on an earlier tick but not yet deleted — e.g. this is a
            // client-predicted ECS still waiting for the server's authoritative deletion to
            // arrive over the delta stream (see CompleteRecall). Left alone entirely so
            // CompleteRecall (and its resource refund) fires exactly once per channel, not
            // once per tick it happens to still be sitting at 0.
            if (recall.TicksRemaining <= 0) return;

            recall.TicksRemaining--;
            ecs.Delta.MarkComponentDirty(id, typeof(RecallingComponent));

            if (recall.TicksRemaining <= 0)
                _completed.Add(id);
        });

        // Entity deletion must be server-authoritative only (mirrors LifetimeSystem/
        // ModifierSystem/DeathRequest — predicted-only deletion would desync a client from
        // the server's authoritative entity set), but the resource refund itself is safe to
        // predict/replay on both, exactly like OnDeathResourceDropSystem's own grant.
        bool isServer = NetworkManager.instance == null || NetworkManager.instance.IsServer;
        foreach (ulong id in _completed)
            CompleteRecall(ecs, id, isServer);
    }

    private static void CompleteRecall(ECS ecs, ulong entityId, bool isServer)
    {
        if (!ecs.HasEntity(entityId)) return;

        RefundResourceValue(ecs, entityId);

        if (isServer)
            ecs.DeleteEntity(entityId);
    }

    private static void RefundResourceValue(ECS ecs, ulong entityId)
    {
        ComponentStore<ResourceValueComponent> valueStore = ecs.GetComponentStore<ResourceValueComponent>();
        if (valueStore == null || !valueStore.HasComponent(entityId)) return;

        ResourceValueComponent value = valueStore.GetComponent(entityId);
        ulong resourceEntityId = ResourceHelper.FindPlayerResourcesEntity(ecs, value.OwnerPlayerId);
        if (resourceEntityId == 0) return;

        PositionQuery.TryGet(ecs, entityId, out float x, out float y);

        Refund(ecs, resourceEntityId, ResourceType.Wood, value.Wood, DefaultRefundRatio, x, y);
        Refund(ecs, resourceEntityId, ResourceType.Stone, value.Stone, DefaultRefundRatio, x, y);
        Refund(ecs, resourceEntityId, ResourceType.Metal, value.Metal, DefaultRefundRatio, x, y);
        Refund(ecs, resourceEntityId, ResourceType.Gems, value.Gems, DefaultRefundRatio, x, y);
        Refund(ecs, resourceEntityId, ResourceType.Soulstones, value.Soulstones, SoulstoneRefundRatio, x, y);
        Refund(ecs, resourceEntityId, ResourceType.Gold, value.Gold, DefaultRefundRatio, x, y);
    }

    private static void Refund(ECS ecs, ulong resourceEntityId, ResourceType type, int amount, float ratio, float x, float y)
    {
        int refund = Mathf.FloorToInt(amount * ratio);
        if (refund <= 0) return;

        ecs.Requests.Process(new ResourcesAdded(resourceEntityId, type, refund) { X = x, Y = y }, ecs);
    }
}
