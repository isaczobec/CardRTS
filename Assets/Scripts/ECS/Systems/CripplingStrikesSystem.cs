// Purely event-driven — no per-tick work of its own. Subscribes to DamageRequest's
// "executed" notification (mirrors GiantsbaneSystem/CleaveSystem/VengefulSpiritsSystem's own
// shape) and, once a DIRECT hit is confirmed (see DamageProcType), visits EVERY active
// CripplingStrikesSourceComponent-carrying modifier targeting the DEALER (buying Crippling
// Strikes more than once on the same card would create a separate modifier entity per
// purchase, but see CripplingStrikesUpgrade.MaxStackCount, which caps that at 1) and applies
// a Chilled-style slow to whoever was hit — reuses ModifierID.Chilled/
// RenderableModifierType.Chilled (same icon/behavior a Chilled-from-a-projectile debuff
// already has — see ProjectileOnHitSystem.ApplySlow, which this mirrors exactly), and, like
// that effect, deliberately does NOT stack: a target already slowed just has its existing
// modifier's duration reset back to full instead of a hit adding a separate one (which
// StatModifierSystem would otherwise sum, stacking the slow far past the intended 60%).
public static class CripplingStrikesSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
        => ecs.Requests.SubscribeExecuted<DamageRequest>(OnDamageExecuted);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void OnDamageExecuted(DamageRequest request, ECS ecs)
    {
        if (request.ProcType != DamageProcType.Direct) return;
        if (request.Amount <= 0) return;

        ulong dealerId = request.DealerEntityId;
        if (dealerId == 0 || dealerId == DamageRequest.NO_DEALER_ENTITYID) return;

        ulong targetId = request.EntityId;

        ModifierQuery.ForEachActiveModifierId<CripplingStrikesSourceComponent>(ecs, dealerId, sourceModifierId =>
            ApplySlow(ecs, sourceModifierId, targetId));
    }

    private static void ApplySlow(ECS ecs, ulong sourceModifierId, ulong targetId)
    {
        ComponentStore<CripplingStrikesSourceComponent> sourceStore = ecs.GetComponentStore<CripplingStrikesSourceComponent>();
        if (sourceStore == null) return;
        CripplingStrikesSourceComponent source = sourceStore.GetComponent(sourceModifierId);

        int durationTicks = TickManager.SecondsToTicks(source.DurationSeconds);

        ulong existingModifierId = FindActiveModifierId(ecs, targetId, ModifierID.Chilled);
        if (existingModifierId != 0)
        {
            ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
            ref ModifierComponent existing = ref modifierStore.GetComponent(existingModifierId);
            existing.TicksRemaining = durationTicks;
            ecs.Delta.MarkComponentDirty(existingModifierId, typeof(ModifierComponent));
            return;
        }

        EntityHandle modifier = ecs.CreateEntity();
        ecs.AddComponent(modifier.Id, new ModifierComponent
        {
            TargetEntityId = targetId,
            TicksRemaining = durationTicks,
            ModifierID     = ModifierID.Chilled,
        });
        ecs.AddComponent(modifier.Id, new StatModifierComponent
        {
            SpeedRatioBonus = -source.SlowRatio,
        });
        ecs.AddComponent(modifier.Id, new RenderableModifierComponent
        {
            Type = RenderableModifierType.Chilled,
        });
    }

    // Mirrors ProjectileOnHitSystem's own private FindActiveModifierId — not shared/exposed
    // from there, so this is its own small local copy (same as CorrosionSystem's own).
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
