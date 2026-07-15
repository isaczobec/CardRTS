using System;

// Subscribes to each of the six StatRequest kinds (GetMaxHealthRequest, GetSpeedRequest,
// ...) and, for every StatModifierComponent-carrying entity whose paired ModifierComponent
// targets the requested entity and is currently active (ModifierQuery.IsActive — false
// while e.g. still mid-deployment, or once its TicksRemaining has expired), folds that
// modifier's ratio/additive bonus for the matching stat into the request. Does no per-tick
// work of its own — expiring/deleting a modifier entity is ModifierSystem's job, this only
// ever reacts to a StatsQuery call processing a request — so Execute is a no-op and all the
// real work happens in Setup's subscriptions.
public static class StatModifierSystem
{
    public static readonly GlobalSystem Instance = new GlobalSystem(Execute, Setup);

    private static void Execute(ECS ecs, FlagEventManager flagEvents) { }

    private static void Setup(ECS ecs)
    {
        Subscribe<GetMaxHealthRequest>(ecs, m => (m.MaxHealthRatioBonus, m.MaxHealthAdditiveBonus));
        Subscribe<GetSpeedRequest>(ecs, m => (m.SpeedRatioBonus, m.SpeedAdditiveBonus));
        Subscribe<GetRangeRequest>(ecs, m => (m.RangeRatioBonus, m.RangeAdditiveBonus));
        Subscribe<GetArmorRequest>(ecs, m => (m.ArmorRatioBonus, m.ArmorAdditiveBonus));
        Subscribe<GetDamageStatRequest>(ecs, m => (m.DamageRatioBonus, m.DamageAdditiveBonus));
        Subscribe<GetAttackSpeedRequest>(ecs, m => (m.AttackSpeedRatioBonus, m.AttackSpeedAdditiveBonus));
    }

    private static void Subscribe<T>(ECS ecs, Func<StatModifierComponent, (float ratio, float additive)> selector) where T : StatRequest
    {
        ecs.Requests.Subscribe<T>((req, innerEcs) => Apply(innerEcs, req, selector));
    }

    private static void Apply<T>(ECS ecs, T req, Func<StatModifierComponent, (float ratio, float additive)> selector) where T : StatRequest
    {
        ComponentStore<ModifierComponent> modifierStore = ecs.GetComponentStore<ModifierComponent>();
        ComponentStore<StatModifierComponent> statModifierStore = ecs.GetComponentStore<StatModifierComponent>();
        if (modifierStore == null || statModifierStore == null) return;

        modifierStore.ForEach((ulong id) =>
        {
            if (!statModifierStore.HasComponent(id)) return;
            if (modifierStore.GetComponent(id).TargetEntityId != req.EntityId) return;
            if (!ModifierQuery.IsActive(ecs, id)) return;

            (float ratio, float additive) = selector(statModifierStore.GetComponent(id));
            req.RatioMultiplier += ratio;
            req.AdditiveBonus += additive;
        });
    }
}
