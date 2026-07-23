// Permanent (TicksRemaining = int.MaxValue, no ActivatableComponent — see
// IronKnightCard's PeriodicDamageReductionComponent for the same shape) modifier: attach
// alongside a ModifierComponent targeting a troop to increase BonusRatio (e.g. 0.2 = +20%)
// of its dealt damage whenever the target is a building — see BuildingDamageBonusSystem,
// which subscribes to DamageRequest and checks for BuildingComponent before applying this.
public struct BuildingDamageBonusComponent : IComponent
{
    public float BonusRatio;
}
