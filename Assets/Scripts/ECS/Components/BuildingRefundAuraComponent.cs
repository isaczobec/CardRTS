// Innate trait (not a modifier) — lives directly on a troop, same idiom as
// OnHitScheduleComponent/OnKillScheduleComponent (see their own doc comments for why some
// troop traits live directly on the troop rather than as a separate ModifierComponent-
// carrying entity). Checked by BuildingRefundHelper.TryRefund, called from
// SpawnAtPointCardPlaySystem right after a card's own spawn — if the newly-spawned entity is
// a building (has BuildingComponent) owned by the same player as this troop and landed
// within RangeMultiplier x this troop's own Range stat, RefundRatio (rounded down, per
// resource) of that building's own ResourceValueComponent is refunded back to its owner.
// See ConstructionWorkerCard, the only card that grants this today.
public struct BuildingRefundAuraComponent : IComponent
{
    public float RangeMultiplier;
    public float RefundRatio;
}
