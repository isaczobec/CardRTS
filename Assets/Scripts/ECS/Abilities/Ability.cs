using System;

// Which AbilityUsed*Input kind an ability responds to — determines which of Ability's
// Execute* lambdas AbilitySystem expects to be non-null for it.
public enum AbilityType
{
    // Cast via AbilityUsedInput — fires the instant the input is processed, no location.
    Instant,

    // Cast via AbilityUsedAtLocationInput — needs a world point within Range of the
    // casting entity.
    TargetLocation,
}

// Data + behavior for one ability, looked up by ID via AbilityManager. Not subclassed —
// every ability is a plain instance of this class built with an object initializer (see
// AbilityManager); Type says which input kind it responds to, and exactly one of the
// Execute* lambdas below should be non-null to match. AbilitySystem rejects an input whose
// corresponding lambda is null, so an ability only ever needs to fill in the one it uses.
public class Ability
{
    public AbilityType Type;

    // World/tile units. Only consulted for AbilityType.TargetLocation — AbilitySystem
    // rejects an AbilityUsedAtLocationInput whose point is further than this from the
    // casting entity's own position before ExecuteAtLocation runs. Unused (leave 0) for
    // AbilityType.Instant.
    public float Range;

    // Exactly one of these should be non-null, matching Type. Left null for whichever
    // input kind this ability doesn't apply to; AbilitySystem checks for that and rejects
    // an input whose matching lambda is missing instead of throwing.
    public Action<ECS, AbilityUsedInput> ExecuteInstant;
    public Action<ECS, AbilityUsedAtLocationInput> ExecuteAtLocation;
}
