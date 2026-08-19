// Attached to a modifier entity (ModifierComponent.TargetEntityId = the affected player's OWN
// PlayerResourcesComponent entity, not a troop; no icon — see ResourceGainDebuffSystem's own
// doc comment) to temporarily throttle that player's resource gains. See
// ResourceGainDebuffSystem, which subscribes to ResourcesAdded and scales Multiplier down by
// MultiplierRatio for Wood/Stone/Metal/Gold gains while an active modifier of this kind
// targets the gaining player. Nothing currently grants this (see ResourceGainDebuffSystem's
// own comment) — kept wired for a future source.
public struct ResourceGainDebuffComponent : IComponent
{
    // Fraction of a normal Wood/Stone/Metal/Gold gain still received while this is active
    // (e.g. 0.3 = -70%). Gems/Soulstones gains are untouched — see ResourceGainDebuffSystem.
    public float MultiplierRatio;
}
