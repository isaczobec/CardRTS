using System;

// Which relationship(s), relative to a scanning entity's own OwnerPlayerId, qualify as a
// match for PeriodicAreaEffectComponent's periodic nearby-entity scan — see
// PeriodicAreaEffectSystem. Combinable (e.g. Enemy | Neutral) since some effects care about
// more than one relationship at once.
[Flags]
public enum AreaTargetFlags : byte
{
    None     = 0,
    Friendly = 1 << 0,
    Enemy    = 1 << 1,
    Neutral  = 1 << 2,
}
