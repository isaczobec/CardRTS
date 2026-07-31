// Innate trait (not a modifier) — lives directly on a troop, same idiom as
// BuildingRefundAuraComponent. Checked by ResourceDropBoostSystem, which subscribes to
// ResourcesAdded: any positional resource drop (ResourcesAdded.X/Y set — e.g. a harvested
// tree/rock/ore, or a troop's on-death gold drop) landing within RangeMultiplier x this
// troop's own Range stat has its Multiplier scaled up by (1 + BoostRatio). See
// StrategyConsultantCard, the only card that grants this today.
public struct ResourceDropBoostComponent : IComponent
{
    public float RangeMultiplier;
    public float BoostRatio;
}
