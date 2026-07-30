using UnityEngine;

// Mitigates a DamageRequest's Amount by the target's Armor (DamageType.Normal) or
// SpellResist (DamageType.Spell) stat before Execute applies it to HealthComponent — same
// diminishing-returns formula either way: postMitigationDamage = 100 / (100 + stat) * damage
// (0 = no mitigation, 100 = half damage, and so on). Subscribed once via Setup (see
// RequestManager's Subscribe/Flush — every DamageRequest runs subscribed callbacks before
// its own Execute), so this has no per-tick work of its own; the actual mitigation happens
// whenever DamageResolutionSystem flushes pending requests. Registered as a GlobalSystem
// purely for the Setup hook.
public static class ArmorMitigationSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Setup(ECS ecs)
    {
        ecs.Requests.Subscribe<DamageRequest>(ApplyMitigation);
    }

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void ApplyMitigation(DamageRequest request, ECS ecs)
    {
        if (request.PreMitigated) return;

        int mitigationStat = request.Type == DamageType.Spell
            ? StatsQuery.GetSpellResist(ecs, request.EntityId, 0)
            : StatsQuery.GetArmor(ecs, request.EntityId, 0);

        if (mitigationStat == StatsComponent.STAT_NA || mitigationStat <= 0) return;

        float mitigated = 100f / (100f + mitigationStat) * request.Amount;
        request.Amount = Mathf.Max(0, Mathf.RoundToInt(mitigated));
    }
}
