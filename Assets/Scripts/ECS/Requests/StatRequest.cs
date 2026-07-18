// Shared base for a per-stat query request (see GetMaxHealthRequest/GetSpeedRequest/...
// below). RequestManager.Process runs every subscriber synchronously; each contributes to
// RatioMultiplier (summed — starts at 1, meaning "no change", so two separate +10%/+20%
// buffs combine into +30% rather than compounding multiplicatively into +32%) and/or
// AdditiveBonus (summed — starts at 0). StatsQuery.Resolve then combines them as
// BaseValue * RatioMultiplier + AdditiveBonus (the multiplier applied before the additive
// bonus) and rounds to the nearest int. See StatModifierComponent/StatModifierSystem for
// the actual bonus contributors.
//
// Fields are mutable (unlike IsActiveRequest/CanTakeActionsRequest's readonly-after-
// construction EntityId) so StatsQuery.Resolve can build one generically via `new T()` +
// object initializer for any of the six stat kinds below, instead of duplicating the same
// resolve logic six times over.
public abstract class StatRequest : Request
{
    public ulong EntityId;
    public int BaseValue;
    public float RatioMultiplier = 1f;
    public float AdditiveBonus;

    public override void Execute(ECS ecs) { }
}

public class GetMaxHealthRequest : StatRequest { }
public class GetSpeedRequest : StatRequest { }
public class GetRangeRequest : StatRequest { }
public class GetArmorRequest : StatRequest { }
public class GetSpellResistRequest : StatRequest { }

// Named GetDamageStatRequest, not GetDamageRequest, to avoid colliding with the existing
// DamageRequest — a "deal N damage to entity X" action request, unrelated to reading the
// Damage stat.
public class GetDamageStatRequest : StatRequest { }
public class GetAttackSpeedRequest : StatRequest { }
