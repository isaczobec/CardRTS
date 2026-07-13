public struct BuildingComponent : IComponent
{
    // Troops that aren't currently moving get instantly relocated outside this radius
    // (from the building's PositionComponent) — see BuildingBlockingSystem.
    public float BlockRadius;

    // Modifies how far cards may be played from this specific building — see
    // Card.MaxDistanceFromFriendlyBuilding and BuildingRangeHelper. Multiplier is applied
    // first, then Bonus is added: effectiveRange = card.MaxDistanceFromFriendlyBuilding *
    // CardPlayRangeMultiplier + CardPlayRangeBonus. Every spawn site must set
    // CardPlayRangeMultiplier explicitly (1 = no change) — the struct-default 0 would
    // silently zero out this building's play range entirely.
    public float CardPlayRangeMultiplier;
    public float CardPlayRangeBonus;
}
