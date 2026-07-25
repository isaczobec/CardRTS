// Generic, reusable "periodically scan for nearby entities matching Targets and invoke a
// registered AreaEffectType callback on each" building block — see PeriodicAreaEffectSystem
// for how the scan itself works and how effects are registered/dispatched. Generalizes
// DamageAuraComponent's own "pulse everything in range" shape with owner-relationship
// filtering (friendly/enemy/neutral) and a pluggable effect instead of a hardcoded damage
// pulse, so new area effects (e.g. Healer Guardian's heal aura) don't need their own bespoke
// scan/query code.
//
// Can be attached either directly to a permanent troop/building (mirrors DamageAuraComponent
// — the entity itself is the scan's source) or to a separate modifier entity paired with a
// ModifierComponent (the ModifierComponent's own TargetEntityId is the scan's source instead)
// — see PeriodicAreaEffectSystem.ResolveSource.
public struct PeriodicAreaEffectComponent : IComponent
{
    // Multiplier applied to the source entity's Range stat to get the actual scan radius.
    public float RangeMultiplier;

    public int PeriodTicks;
    public int TicksUntilNextProc;

    // Which relationship(s), relative to the source's own OwnerPlayerId, qualify as scan
    // targets.
    public AreaTargetFlags Targets;

    // Which registered callback (see PeriodicAreaEffectSystem.RegisterEffect) fires once per
    // qualifying entity found each proc.
    public AreaEffectType EffectType;

    // Generic payload passed straight through to the registered callback — same idea as
    // ScheduledCallComponent's Param0-3, kept generic so this component stays reusable across
    // completely different effects.
    public float Param0;
    public float Param1;
    public int Param2;
}
