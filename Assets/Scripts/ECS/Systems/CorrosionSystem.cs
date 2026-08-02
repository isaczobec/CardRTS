using UnityEngine;

// Two independent responsibilities, both purely event-driven (no per-tick work of its own):
//
// 1. Subscribes to DamageRequest's "executed" notification (mirrors GiantsbaneSystem/
//    CleaveSystem's own shape) and, once a hit is confirmed, visits EVERY active
//    CorrosionSourceComponent-carrying modifier targeting the DEALER (see
//    ModifierQuery.ForEachActiveModifierId — buying Corrosion more than once on the same
//    card creates a separate source modifier entity per purchase, so each one refreshes/
//    stacks the target's debuff independently) and applies/refreshes a SINGLE shared
//    CorrosionComponent-carrying modifier (ModifierID.Corrosion) on whoever was hit —
//    mirrors ProjectileOnHitSystem.ApplyScorch's own "refresh the existing one, else create
//    it" shape, refreshing TicksRemaining back to the full DurationSeconds and incrementing
//    Stacks (capped at MaxStacks) on every qualifying hit, from ANY Corrosion-wielding
//    attacker.
//
// 2. Subscribes to GetArmorRequest and, for every active Corrosion modifier targeting the
//    requested entity, subtracts (Stacks / MaxStacks) x MaxArmorReduction from AdditiveBonus
//    — i.e. the reduction scales linearly from 0 up to MaxArmorReduction as Stacks goes from
//    0 to MaxStacks. Explicit design ask: Corrosion must never push armor into negative
//    territory by itself, so the total reduction is clamped to req.BaseValue (the entity's
//    own raw Armor stat, read straight off StatsComponent before any modifier — see
//    StatsQuery.Resolve) — a self-contained guarantee about THIS effect's own contribution,
//    not a claim about the fully-resolved armor after every other modifier in the same
//    request has also been applied.
public static class CorrosionSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);
        ecs.Requests.Subscribe<GetArmorRequest>(ApplyArmorReduction);
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        if (request.Amount <= 0) return;

        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ulong targetId = request.EntityId;

        ModifierQuery.ForEachActiveModifierId<CorrosionSourceComponent>(ecs, dealerId, sourceModifierId =>
            ApplyCorrosionStack(ecs, sourceModifierId, targetId));
    }

    private static void ApplyCorrosionStack(ECS ecs, ulong sourceModifierId, ulong targetId)
    {
        ComponentStore<CorrosionSourceComponent> sourceStore = ecs.GetComponentStore<CorrosionSourceComponent>();
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<CorrosionComponent> corrosionStore = ecs.GetComponentStore<CorrosionComponent>();
        if (sourceStore == null || modifierStore == null || corrosionStore == null) return;

        CorrosionSourceComponent source = sourceStore.GetComponent(sourceModifierId);
        int durationTicks = TickManager.SecondsToTicks(source.DurationSeconds);

        ulong existingModifierId = FindActiveModifierId(ecs, targetId, ModifierID.Corrosion);
        if (existingModifierId != 0)
        {
            ref ModifierComponent existing = ref modifierStore.GetComponent(existingModifierId);
            existing.TicksRemaining = durationTicks;
            ecs.Delta.MarkComponentDirty(existingModifierId, typeof(ModifierComponent));

            ref CorrosionComponent corrosion = ref corrosionStore.GetComponent(existingModifierId);
            corrosion.Stacks = Mathf.Min(corrosion.Stacks + 1, corrosion.MaxStacks);
            ecs.Delta.MarkComponentDirty(existingModifierId, typeof(CorrosionComponent));
            return;
        }

        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = targetId,
            TicksRemaining = durationTicks,
            ModifierID     = ModifierID.Corrosion,
        });
        ecs.AddComponent(modifier.Id, new CorrosionComponent
        {
            Stacks            = 1,
            MaxStacks         = source.MaxStacks,
            MaxArmorReduction = source.MaxArmorReduction,
        });
        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.Corrosion,
        });
    }

    private static void ApplyArmorReduction(GetArmorRequest req, ECS ecs)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<CorrosionComponent> corrosionStore = ecs.GetComponentStore<CorrosionComponent>();
        if (modifierStore == null || corrosionStore == null) return;

        float totalReduction = 0f;
        corrosionStore.ForEach((ulong modifierId) =>
        {
            if (!modifierStore.HasComponent(modifierId)) return;
            if (modifierStore.GetComponent(modifierId).TargetEntityId != req.EntityId) return;
            if (!ModifierQuery.IsActive(ecs, modifierId)) return;

            CorrosionComponent corrosion = corrosionStore.GetComponent(modifierId);
            if (corrosion.MaxStacks <= 0) return;
            totalReduction += (float)corrosion.Stacks / corrosion.MaxStacks * corrosion.MaxArmorReduction;
        });

        if (totalReduction <= 0f) return;

        float clampedReduction = Mathf.Min(totalReduction, Mathf.Max(0f, req.BaseValue));
        req.AdditiveBonus -= clampedReduction;
    }

    // Mirrors ProjectileOnHitSystem's own private FindActiveModifierId — not shared/exposed
    // from there (that copy already exists purely for that file's own Chilled/Scorched
    // lookups), so this is its own small local copy, same as that file's own doc comment
    // notes other systems would need to do.
    private static ulong FindActiveModifierId(ECS ecs, ulong targetId, ModifierID modifierId)
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        if (modifierStore == null) return 0;

        ulong found = 0;
        modifierStore.ForEach((ulong candidateId) =>
        {
            if (found != 0) return;
            ModifierComponent modifier = modifierStore.GetComponent(candidateId);
            if (modifier.ModifierID != modifierId) return;
            if (modifier.TargetEntityId != targetId) return;
            if (!ModifierQuery.IsActive(ecs, candidateId)) return;
            found = candidateId;
        });
        return found;
    }
}
